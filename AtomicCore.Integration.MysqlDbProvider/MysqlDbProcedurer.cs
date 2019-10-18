﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;
using AtomicCore.DbProvider;
using System.Data;

namespace AtomicCore.Integration.MysqlDbProvider
{
    /// <summary>
    /// 执行Mysql存储过程
    /// </summary>
    public class MysqlDbProcedurer : IDbProcedurer
    {
        #region Constructors

        /// <summary>
        /// 数据库链接字符串 
        /// </summary>
        private string _dbConnString = string.Empty;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dbConnString">链接字符串</param>
        public MysqlDbProcedurer(string dbConnString)
        {
            this._dbConnString = dbConnString;
        }

        #endregion

        #region IBizSqlProcedurer

        /// <summary>
        /// 在当前数据源下执行脚本或命令
        /// </summary>
        /// <param name="inputData">输入的参数对象</param>
        /// <returns></returns>
        public DbCalculateRecord Execute(DbExecuteInputBase inputData)
        {
            DbCalculateRecord result = new DbCalculateRecord();
            if (string.IsNullOrEmpty(inputData.CommandText))
            {
                result.AppendError("执行的脚本命令为空，请传入命令后再调用执行");
                return result;
            }

            //执行读取参数
            List<MySqlParameter> sqlParameters = null;
            MySqlParameter param = null;
            IEnumerable<object> objParams = inputData.GetParameterCollection();
            IEnumerable<MysqlParameterDesc> parameters = null;
            if (null != objParams)
            {
                parameters = objParams.Cast<MysqlParameterDesc>();
            }

            if (parameters != null && parameters.Count() > 0)
            {
                sqlParameters = new List<MySqlParameter>();
                foreach (var msParam in parameters)
                {
                    if (null != msParam)
                    {
                        param = new MySqlParameter(msParam.Name, msParam.Value);
                        param.Direction = (ParameterDirection)Enum.Parse(typeof(ParameterDirection), Convert.ToInt32(msParam.Direction).ToString());

                        sqlParameters.Add(param);
                    }
                }
            }
            //Debug初始化
            result.DebugInit(new StringBuilder(inputData.CommandText), MysqlGrammarRule.C_ParamChar, null == sqlParameters ? null : sqlParameters.ToArray());

            //执行数据库查询
            using (MySqlConnection connection = new MySqlConnection(this._dbConnString))
            {
                using (MySqlCommand command = new MySqlCommand())
                {
                    command.Connection = connection;
                    command.CommandTimeout = 0;
                    command.CommandText = inputData.CommandText;
                    command.CommandType = inputData.CommandType;
                    if (sqlParameters != null && sqlParameters.Count > 0)
                    {
                        command.Parameters.AddRange(sqlParameters.ToArray());
                    }
                    //尝试打开数据库链接
                    if (this.TryOpenDbConnection<DbCalculateRecord>(connection, ref result))
                    {
                        if (inputData.HasReturnRecords)
                        {
                            //尝试执行语句返回DataReader
                            DbDataReader reader = this.TryExecuteReader<DbCalculateRecord>(command, ref result);
                            if (reader != null && reader.HasRows)
                            {
                                List<DbRowRecord> rowDataList = new List<DbRowRecord>();//设置所有的行数据容器
                                DbRowRecord rowItem = null;//设置行数据对象
                                DbColumnRecord columnItem = null;//列数据对象
                                while (reader.Read())
                                {
                                    rowItem = new DbRowRecord();
                                    for (int i = 0; i < reader.FieldCount; i++)
                                    {
                                        object objVal = reader.GetValue(i);
                                        if (objVal != null && objVal != DBNull.Value)
                                        {
                                            columnItem = new DbColumnRecord();
                                            columnItem.Name = reader.GetName(i);
                                            columnItem.Value = objVal;

                                            //在行数据对象中装载列数据
                                            rowItem.Add(columnItem);
                                        }
                                    }
                                    rowDataList.Add(rowItem);
                                }
                                result.Record = rowDataList;

                                //释放资源，关闭连结
                                this.DisposeReader(reader);
                            }
                        }
                        else
                        {
                            int i = command.ExecuteNonQuery();

                            result.Record = new List<DbRowRecord>()
                            {
                                new DbRowRecord()
                                {
                                    new DbColumnRecord()
                                    {
                                        Name = string.Empty,
                                        Value= i
                                    }
                                }
                            };
                        }

                        //将存储过程的输出参数返回
                        if (null != parameters && parameters.Count() > 0)
                        {
                            IEnumerable<MysqlParameterDesc> outParams = parameters.Where(d => d.Direction == MysqlParameterDirection.Output || d.Direction == MysqlParameterDirection.InputOutput);
                            if (null != outParams && outParams.Count() > 0)
                            {
                                foreach (var item in outParams)
                                {
                                    MySqlParameter dbParam = sqlParameters.FirstOrDefault(d => d.ParameterName == item.Name);
                                    if (null != dbParam)
                                    {
                                        item.Value = dbParam.Value;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 尝试打开数据库链接
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="connection"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        private bool TryOpenDbConnection<T>(MySqlConnection connection, ref T result)
            where T : ResultBase
        {
            bool isOpen = false;
            try
            {
                connection.Open();
                isOpen = true;
            }
            catch (Exception ex)
            {
                isOpen = false;
                result.AppendError("数据库无法打开!");
                result.AppendException(ex);
            }
            return isOpen;
        }

        /// <summary>
        /// 尝试执行DBDataReader,可能返回为null值
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="command"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        private DbDataReader TryExecuteReader<T>(MySqlCommand command, ref T result)
            where T : ResultBase
        {
            DbDataReader reader = null;
            try
            {
                reader = command.ExecuteReader();
            }
            catch (Exception ex)
            {
                reader = null;
                result.AppendError("sql语句执行错误，" + command.CommandText);
                result.AppendException(ex);
            }
            return reader;
        }

        /// <summary>
        /// 执行关闭并且释放资源
        /// </summary>
        /// <param name="reader"></param>
        public void DisposeReader(DbDataReader reader)
        {
            //释放资源，关闭连结
            using (reader as IDisposable) { }
        }

        #endregion
    }
}