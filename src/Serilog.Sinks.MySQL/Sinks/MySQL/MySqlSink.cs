// Copyright 2017 Zethian Inc.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.Batch;
using Serilog.Sinks.Extensions;

namespace Serilog.Sinks.MySQL
{
    internal class MySqlSink : BatchProvider, ILogEventSink
    {
        private readonly string _connectionString;
        private readonly bool _storeTimestampInUtc;
        private readonly string _tableName;
        private readonly int _rollingInterval;
        private readonly int _retainedLogCountLimit;

        public MySqlSink(
            string connectionString,
            string tableName = "Logs",
            int rollingInterval = 4,
            int retainedLogCountLimit = 72,
            bool storeTimestampInUtc = false,
            uint batchSize = 100) : base((int)batchSize)
        {
            _connectionString = connectionString;
            _tableName = tableName;
            _rollingInterval = rollingInterval;
            _retainedLogCountLimit = retainedLogCountLimit;
            _storeTimestampInUtc = storeTimestampInUtc;

            var sqlConnection = GetSqlConnection();
            CreateTable(sqlConnection);

            Thread thread = new Thread(new ThreadStart(cleanLogs));
            thread.Start();
        }

        private void cleanLogs()
        {
            DateTime lastDay = DateTime.Now.Date.AddDays(-1);
            while (true)
            {
                try
                {
                    if (DateTime.Now.Date <= lastDay) continue;
                    if (DateTime.Now.Hour < 2) continue;
                    if (_rollingInterval == 0) continue;

                    var sqlConnection = GetSqlConnection();
                    var cmd = sqlConnection.CreateCommand();

                    DateTime cleanTime = DateTime.Now;
                    switch(_rollingInterval)
                    {
                        case 1: //year
                            cleanTime = cleanTime.AddYears(-_retainedLogCountLimit);
                            break;
                        case 2: //month
                            cleanTime = cleanTime.AddMonths(-_retainedLogCountLimit);
                            break;
                        case 3: //day
                            cleanTime = cleanTime.AddDays(-_retainedLogCountLimit);
                            break;
                        case 4: //hour
                            cleanTime = cleanTime.AddHours(-_retainedLogCountLimit);
                            break;
                        case 5: //mintue
                            cleanTime = cleanTime.AddMinutes(-_retainedLogCountLimit);
                            break;
                    }
                    string sql = string.Format("delete from `{0}` where time < '{1}'", _tableName, cleanTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();

                    sql = string.Format("OPTIMIZE TABLE `{0}`", _tableName);
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();

                    lastDay = DateTime.Now.Date;
                }
                finally
                {
                    Thread.Sleep(60 * 1000);
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            PushEvent(logEvent);
        }

        private MySqlConnection GetSqlConnection()
        {
            try
            {
                var conn = new MySqlConnection(_connectionString);
                conn.Open();
                return conn;
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(ex.Message);
                return null;
            }
        }

        private MySqlCommand GetInsertCommand(MySqlConnection sqlConnection)
        {
            //var tableCommandBuilder = new StringBuilder();
            //tableCommandBuilder.Append($"INSERT INTO  {_tableName} (");
            //tableCommandBuilder.Append("Timestamp, Level, Message, Exception, Properties) ");
            //tableCommandBuilder.Append("VALUES (@ts, @lvel, @msg, @ex, @prop)");

            //var cmd = sqlConnection.CreateCommand();
            //cmd.CommandText = tableCommandBuilder.ToString();

            //cmd.Parameters.Add(new MySqlParameter("@ts", MySqlDbType.VarChar));
            //cmd.Parameters.Add(new MySqlParameter("@lvel", MySqlDbType.VarChar));
            //cmd.Parameters.Add(new MySqlParameter("@msg", MySqlDbType.VarChar));
            //cmd.Parameters.Add(new MySqlParameter("@ex", MySqlDbType.VarChar));
            //cmd.Parameters.Add(new MySqlParameter("@prop", MySqlDbType.VarChar));

            var tableCommandBuilder = new StringBuilder();
            tableCommandBuilder.Append($"INSERT INTO  {_tableName} (");
            tableCommandBuilder.Append("Time, Level, Index1, Index2, Index3, Message, Exception, Properties) ");
            tableCommandBuilder.Append("VALUES (@ts, @lvel, @idx1, @idx2, @idx3, @msg, @ex, @prop)");

            var cmd = sqlConnection.CreateCommand();
            cmd.CommandText = tableCommandBuilder.ToString();

            cmd.Parameters.Add(new MySqlParameter("@ts", MySqlDbType.DateTime));
            cmd.Parameters.Add(new MySqlParameter("@lvel", MySqlDbType.VarChar));
            cmd.Parameters.Add(new MySqlParameter("@idx1", MySqlDbType.VarChar));
            cmd.Parameters.Add(new MySqlParameter("@idx2", MySqlDbType.VarChar));
            cmd.Parameters.Add(new MySqlParameter("@idx3", MySqlDbType.VarChar));
            cmd.Parameters.Add(new MySqlParameter("@msg", MySqlDbType.VarChar));
            cmd.Parameters.Add(new MySqlParameter("@ex", MySqlDbType.VarChar));
            cmd.Parameters.Add(new MySqlParameter("@prop", MySqlDbType.VarChar));

            return cmd;
        }

        private void CreateTable(MySqlConnection sqlConnection)
        {
            try
            {
                var tableCommandBuilder = new StringBuilder();
                //tableCommandBuilder.Append($"CREATE TABLE IF NOT EXISTS {_tableName} (");
                //tableCommandBuilder.Append("id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,");
                //tableCommandBuilder.Append("Timestamp VARCHAR(100),");
                //tableCommandBuilder.Append("Level VARCHAR(15),");
                //tableCommandBuilder.Append("Message TEXT,");
                //tableCommandBuilder.Append("Exception TEXT,");
                //tableCommandBuilder.Append("Properties TEXT,");
                //tableCommandBuilder.Append("_ts TIMESTAMP)");

                //var cmd = sqlConnection.CreateCommand();
                //cmd.CommandText = tableCommandBuilder.ToString();
                //cmd.ExecuteNonQuery();

                tableCommandBuilder.Append($"CREATE TABLE IF NOT EXISTS {_tableName} (");
                tableCommandBuilder.Append("id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,");
                tableCommandBuilder.Append("Time Datetime,");
                tableCommandBuilder.Append("Level VARCHAR(15),");
                tableCommandBuilder.Append("Index1 VARCHAR(100),");
                tableCommandBuilder.Append("Index2 VARCHAR(100),");
                tableCommandBuilder.Append("Index3 VARCHAR(100),");
                tableCommandBuilder.Append("Message TEXT,");
                tableCommandBuilder.Append("Exception TEXT,");
                tableCommandBuilder.Append("Properties TEXT)");

                var cmd = sqlConnection.CreateCommand();
                cmd.CommandText = tableCommandBuilder.ToString();
                cmd.ExecuteNonQuery();

                //
                string sql = "";
                sql = string.Format("CREATE INDEX {0}_IDX1 on {0}(Time)", _tableName);
                cmd.CommandText = sql;
                try { cmd.ExecuteNonQuery(); } catch { }

                sql = string.Format("CREATE INDEX {0}_IDX2 on {0}(Time, Index1)", _tableName);
                cmd.CommandText = sql;
                try { cmd.ExecuteNonQuery(); } catch { }

                sql = string.Format("CREATE INDEX {0}_IDX3 on {0}(Time, Index1, Index2)", _tableName);
                cmd.CommandText = sql;
                try { cmd.ExecuteNonQuery(); } catch { }

                sql = string.Format("CREATE INDEX {0}_IDX4 on {0}(Time, Index1, Index2, Index3)", _tableName);
                cmd.CommandText = sql;
                try { cmd.ExecuteNonQuery(); } catch { }

                sql = string.Format("CREATE INDEX {0}_IDX5 on {0}(Time, Level)", _tableName);
                cmd.CommandText = sql;
                try { cmd.ExecuteNonQuery(); } catch { }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(ex.Message);
            }
        }

        protected override async Task<bool> WriteLogEventAsync(ICollection<LogEvent> logEventsBatch)
        {
            try
            {
                using (var sqlCon = GetSqlConnection())
                {
                    using (var tr = await sqlCon.BeginTransactionAsync()
                        .ConfigureAwait(false))
                    {
                        var insertCommand = GetInsertCommand(sqlCon);
                        insertCommand.Transaction = tr;

                        foreach (var logEvent in logEventsBatch)
                        {
                            //insertCommand.Parameters["@ts"]
                            //    .Value = _storeTimestampInUtc
                            //    ? logEvent.Timestamp.ToUniversalTime()
                            //        .ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
                            //    : logEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fffzzz");

                            //insertCommand.Parameters["@lvel"]
                            //    .Value = logEvent.Level.ToString();
                            //insertCommand.Parameters["@msg"]
                            //    .Value = logEvent.MessageTemplate.ToString();
                            //insertCommand.Parameters["@ex"]
                            //    .Value = logEvent.Exception?.ToString();
                            //insertCommand.Parameters["@prop"]
                            //    .Value = logEvent.Properties.Json();

                            insertCommand.Parameters["@ts"].Value = DateTime.Now;

                            string[] ss = logEvent.MessageTemplate.ToString().Split(new char[] { ' ' });

                            insertCommand.Parameters["@lvel"].Value = logEvent.Level.ToString();
                            insertCommand.Parameters["@idx1"].Value = ss.Length > 0 ? ss[0].Substring(0, ss[0].Length >= 100 ? 100 : ss[0].Length) : "";
                            insertCommand.Parameters["@idx2"].Value = ss.Length > 1 ? ss[1].Substring(0, ss[1].Length >= 100 ? 100 : ss[1].Length) : "";
                            insertCommand.Parameters["@idx3"].Value = ss.Length > 2 ? ss[2].Substring(0, ss[2].Length >= 100 ? 100 : ss[2].Length) : "";
                            insertCommand.Parameters["@msg"].Value = logEvent.MessageTemplate.ToString();
                            insertCommand.Parameters["@ex"].Value = logEvent.Exception?.ToString();
                            insertCommand.Parameters["@prop"].Value = logEvent.Properties.Json();

                            await insertCommand.ExecuteNonQueryAsync()
                                .ConfigureAwait(false);
                        }
                        tr.Commit();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(ex.Message);
                return false;
            }
        }
    }
}