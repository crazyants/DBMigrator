﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using DBMigrator.Model;
using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace DBMigrator
{
    public class Database : IDisposable
    {
        private SqlConnection sqlconn;
        private SqlTransaction trans;
        private Logger _logger;
        
        public Database(string servername, string database, string username, string password)
        {
            var connectionString = $"Data Source={servername};Initial Catalog={database};Persist Security Info=True;User ID={username};Password={password};MultipleActiveResultSets=True";
            SetupConnAndLogger(connectionString);
        }

        public Database(string initialCatalog) // string mdfFilePath, 
        {
            var connectionString = $@"Data Source=(localdb)\v11.0;Integrated Security=True;User Instance=False;Initial Catalog={initialCatalog}";
            SetupConnAndLogger(connectionString);
        }
        private void SetupConnAndLogger(string connectionString)
        {
            _logger = Bootstrapper.GetConfiguredServiceProvider().GetRequiredService<Logger>();
            sqlconn = new SqlConnection(connectionString);
        }

        public string CheckDatabaseVersion()
        {
            var version = "0.0.0.0";
            try
            {
                sqlconn.Open();
                //var data = ExecuteCommand("SELECT TOP 1 Version FROM DBVersion order by Date desc");
                var data = ExecuteCommand("SELECT TOP 1 Version FROM DBVersionScripts order by Date desc");
                if (data.HasRows)
                {
                    data.Read();
                    version = data.GetString(0);
                    _logger.Log($"Found existing database version {version}");
                }
                sqlconn.Close();
            }
            catch (Exception ex)
            {
                sqlconn.Close();
                CreateDBVersionTable();
            }
            return version;
        }

        private void CreateDBVersionTable2()
        {
            _logger.Log("Creating DBVersion table");
            ExecuteSingleCommand("CREATE TABLE DBVersion ([ID] [int] IDENTITY(1,1) NOT NULL, Version varchar(max) NOT NULL, Date datetime2 NOT NULL, Log xml NOT NULL, CONSTRAINT [PK_dbo.DBVersion] PRIMARY KEY CLUSTERED ([ID] ASC))");
            ExecuteSingleCommand("CREATE TABLE DBVersionScripts (DBVersionID int NOT NULL, Feature varchar(max) NOT NULL, [Order] int NOT NULL, Script varchar(max) NOT NULL, Type varchar(max) NOT NULL, [Checksum] varchar(max) NOT NULL, ExecutionTime int NOT NULL)");
            ExecuteSingleCommand("ALTER TABLE [DBVersionScripts] WITH CHECK ADD CONSTRAINT [FK.DBVersion.DBVersionScripts_DBVersionID] FOREIGN KEY([DBVersionID]) REFERENCES [DBVersion]([ID])");
        }

        private void CreateDBVersionTable()
        {
            _logger.Log("Creating DBVersion table");
            ExecuteSingleCommand(@"CREATE TABLE DBVersionScripts (
                            [ID] [int] IDENTITY(1,1) NOT NULL, 
                            Date datetime2 NOT NULL, 
                            Version varchar(max) NOT NULL, 
                            Feature varchar(max) NOT NULL, 
                            [Order] int NOT NULL, 
                            Script varchar(max) NOT NULL, 
                            Type varchar(max) NOT NULL, 
                            [ScriptFileChecksum] varchar(max) NOT NULL, 
                            [DatabaseTriggersChecksum] varchar(max) NOT NULL, 
                            [DatabaseTablesAndViewsChecksum] varchar(max) NOT NULL, 
                            [DatabaseFunctionsChecksum] varchar(max) NOT NULL, 
                            [DatabaseStoredProceduresChecksum] varchar(max) NOT NULL, 
                            [DatabaseIndexesChecksum] varchar(max) NOT NULL, 
                            ExecutionTime int NOT NULL)");
        }

        public void UpdateDatabaseVersion(DBVersion version)
        {
            var versionStr = version.Version.ToString();
            _logger.Log($"Updating DBVersion version to {versionStr}");
            //var data = ExecuteCommand($"INSERT INTO DBVersion (Version, Date, Log) OUTPUT Inserted.ID VALUES ('{versionStr}', GETUTCDATE(), '<xml>' + CHAR(13) + '{_logger.log.ToString()}</xml>')");
            
            //using (data)
            //{
            //    data.Read();
            //    version.ID = data.GetInt32(0);
            //}
        }

        public void UpdateLog(DBVersion version)
        {
            //ExecuteCommand($"UPDATE DBVersion SET Log = '<xml>' + CHAR(13) + '{_logger.log.ToString()}</xml>' WHERE ID = {version.ID}");
        }

        public void UpdateDataWithFile(Script script)
        {
            var sw = new Stopwatch();
            sw.Start();
            ExecuteCommand(script.SQL);
            sw.Stop();
            script.ExecutionTime = Convert.ToInt32(sw.ElapsedMilliseconds);
            var databaseTriggersChecksum = GetTriggersChecksum();
            var databaseTablesAndViewsChecksum = GetTablesViewsAndColumnsChecksum();
            var databaseFunctionsChecksum = GetFunctionsChecksum();
            var databaseStoredProceduresChecksum = GetStoredProceduresChecksum();
            var databaseIndexesChecksum = GetIndexesChecksum();
            //ExecuteCommand($"INSERT INTO DBVersionScripts (DBVersionID, [Order], Feature, Script, Type, Checksum, ExecutionTime) VALUES ('{script.Feature.Version.ID}', {script.Order}, '{script.Feature.Name}', '{script.FileName}', '{script.Type.ToString()}', '{script.Checksum}', {script.ExecutionTime})");
            ExecuteCommand($@"INSERT INTO DBVersionScripts (
                                                [Version], 
                                                [Date], 
                                                [Order], 
                                                Feature, 
                                                Script, 
                                                Type, 
                                                ScriptFileChecksum, 
                                                DatabaseTriggersChecksum, 
                                                DatabaseTablesAndViewsChecksum, 
                                                DatabaseFunctionsChecksum, 
                                                DatabaseStoredProceduresChecksum, 
                                                DatabaseIndexesChecksum, 
                                                ExecutionTime) VALUES (
                                                '{script.Feature.Version.Name}',
                                                GETUTCDATE(), 
                                                {script.Order}, 
                                                '{script.Feature.Name}', 
                                                '{script.FileName}', 
                                                '{script.Type.ToString()}', 
                                                '{script.Checksum}', 
                                                '{databaseTriggersChecksum}', 
                                                '{databaseTablesAndViewsChecksum}', 
                                                '{databaseFunctionsChecksum}', 
                                                '{databaseStoredProceduresChecksum}', 
                                                '{databaseIndexesChecksum}', 
                                                {script.ExecutionTime})");
        }

        public void DowngradeDataWithFile(Script script)
        {
            ExecuteCommand(script.SQL);
            ExecuteCommand($"DELETE FROM DBVersionScripts WHERE Script = '{script.RollbackScript.FileName}'");
        }

        public void ExecuteSingleCommand(string cmd)
        {
            sqlconn.Open();
            try
            {
                ExecuteCommand(cmd);
            }
            finally
            {
                sqlconn.Close();
            }
        }

        private SqlDataReader ExecuteCommand(string cmd)
        {
            using (SqlCommand command = new SqlCommand(cmd, sqlconn, trans))
            {
                var result = command.ExecuteReader();
                return result;
            }
        }

        public void BeginTransaction()
        {
            sqlconn.Open();
            trans = sqlconn.BeginTransaction();
        }

        public void CommitTransaction()
        {
            trans.Commit();
            sqlconn.Close();
        }

        public void RollbackTransaction()
        {
            trans.Rollback();
            sqlconn.Close();
        }

        public void Close()
        {
            sqlconn.Close();
        }

        public List<DBVersion> GetDBState() {
            CheckDatabaseVersion();
            sqlconn.Open();
            var result = new List<DBVersion>();
            //var data = ExecuteCommand("SELECT [Version], [Feature], [Order], [Script], [Type], [Checksum], [ExecutionTime] FROM [DBVersion] LEFT JOIN [DbversionScripts] ON [DBVersion].ID = [DbversionScripts].DBVersionID");
            var data = ExecuteCommand("SELECT [Version], [Feature], [Order], [Script], [Type], [ScriptFileChecksum], [ExecutionTime] FROM [DBVersionScripts]");
            while (data.Read())
            {
                var version = data.GetString(0);

                var dbversion = result.FirstOrDefault(v => v.Name == version);
                if (dbversion == null)
                {
                    dbversion = new DBVersion(version);
                    result.Add(dbversion);
                }

                string feature = null;
                if(!data.IsDBNull(1))
                {
                    feature = data.GetString(1);
                    var order = data.GetInt32(2);
                    var scriptFileName = data.GetString(3);
                    var type = data.GetString(4);
                    var checksum = data.GetString(5);
                    var executiontime = data.GetInt32(6);

                    var script = dbversion.AddAndOrGetFeature(feature).AddScript(scriptFileName, order, (Script.SQLTYPE)Enum.Parse(typeof(Script.SQLTYPE), type));
                    script.Checksum = checksum;
                    script.ExecutionTime = executiontime;
                }
            }
            sqlconn.Close();
            return result;
        }
        //http://www.bidn.com/blogs/TomLannen/bidn-blog/2265/using-hashbytes-to-compare-columns
        public string GetTablesViewsAndColumnsChecksum()
        {
            var query = @"SELECT HASHBYTES('SHA1', TABLE_SCHEMA + '|' 
						+ DATA_TYPE + '|' 
						+ TABLE_NAME + '|' 
						+ COLUMN_NAME + '|' 
						+ CAST(ISNULL(NUMERIC_PRECISION, 0) as varchar(max)) + '|' 
						+ CAST(ISNULL(DATETIME_PRECISION, 0) as varchar(max)) + '|' 
						+ CAST(ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) as varchar(max)) + '|' 
                        ) FROM INFORMATION_SCHEMA.COLUMNS";
            
            return CheckSumHelper2(query);
        }

        public int GetStoredProceduresChecksum()
        {
            var query = @"SELECT
                        CHECKSUM_AGG(CHECKSUM
                        ([SPECIFIC_CATALOG]
                              , [SPECIFIC_SCHEMA]
                              , [SPECIFIC_NAME]
                              , [ROUTINE_CATALOG]
                              , [ROUTINE_SCHEMA]
                              , [ROUTINE_NAME]
                              , [ROUTINE_TYPE]
                              , [DATA_TYPE]
                              , [CHARACTER_MAXIMUM_LENGTH]
                              , [CHARACTER_OCTET_LENGTH]
                              , [NUMERIC_PRECISION]
                              , [DATETIME_PRECISION]
                              , [ROUTINE_BODY]
                              , [ROUTINE_DEFINITION]
                              , [IS_DETERMINISTIC]
                              , [SQL_DATA_ACCESS]
                              , [IS_NULL_CALL])) as StoredProceduresChecksum
                         FROM [INFORMATION_SCHEMA].[ROUTINES]
                        WHERE ROUTINE_TYPE = 'PROCEDURE'";

            return CheckSumHelper(query);
        }

        public int GetFunctionsChecksum()
        {
            var query = @"SELECT
                        CHECKSUM_AGG(CHECKSUM
                        ([SPECIFIC_CATALOG]
                              , [SPECIFIC_SCHEMA]
                              , [SPECIFIC_NAME]
                              , [ROUTINE_CATALOG]
                              , [ROUTINE_SCHEMA]
                              , [ROUTINE_NAME]
                              , [ROUTINE_TYPE]
                              , [DATA_TYPE]
                              , [CHARACTER_MAXIMUM_LENGTH]
                              , [CHARACTER_OCTET_LENGTH]
                              , [NUMERIC_PRECISION]
                              , [DATETIME_PRECISION]
                              , [ROUTINE_BODY]
                              , [ROUTINE_DEFINITION]
                              , [IS_DETERMINISTIC]
                              , [SQL_DATA_ACCESS]
                              , [IS_NULL_CALL])) as FunctionsChecksum
                         FROM [INFORMATION_SCHEMA].[ROUTINES]
                        WHERE ROUTINE_TYPE = 'FUNCTION'";

            return CheckSumHelper(query);
        }

        public int GetTriggersChecksum()
        {
            var query = @"SELECT
                        CHECKSUM_AGG(CHECKSUM
                        ([name]
                              , [sys].[all_objects].[object_id]
                              , [principal_id]
                              , [schema_id]
                              , [parent_object_id]
                              , [type]
                              , [type_desc]
                              , [is_ms_shipped]
                              , [is_published]
                              , [is_schema_published]
                              , [definition])) as TriggersChecksum
                        FROM[sys].[all_objects]
                        INNER JOIN[sys].[sql_modules]
                        ON[sys].[sql_modules].[object_id] = [sys].[all_objects].[object_id]
                        WHERE type = 'TR'";

            return CheckSumHelper(query);
        }

        public int GetIndexesChecksum()
        {
            var query = @"SELECT
                        CHECKSUM_AGG(CHECKSUM
                        ([CONSTRAINT_CATALOG]
                          ,[CONSTRAINT_SCHEMA]
                          ,[CONSTRAINT_NAME]
                          ,[TABLE_CATALOG]
                          ,[TABLE_SCHEMA]
                          ,[TABLE_NAME]
                          ,[CONSTRAINT_TYPE]
                          ,[IS_DEFERRABLE]
                          ,[INITIALLY_DEFERRED])) as IndexesChecksum
                        FROM [INFORMATION_SCHEMA].[TABLE_CONSTRAINTS]";

            return CheckSumHelper(query);
        }

        private int CheckSumHelper(string query)
        {
            var result = 0;
            //sqlconn.Open();
            var data = ExecuteCommand(query);
            
            using (data)
            {
                data.Read();
                if(!data.IsDBNull(0))
                    result = data. GetInt32(0);
            }
            //sqlconn.Close();
            return result;
        }

        private string CheckSumHelper2(string query)
        {
            var result = 0;
            //sqlconn.Open();
            var data = ExecuteCommand(query);

            var sha = SHA256.Create();
            var memStream = new MemoryStream();

            using (data)
            {
                while(data.Read())
                {
                    using (var dbStream = data.GetStream(0))
                    {
                        dbStream.CopyTo(memStream);
                    }
                }
            }

            var hash = sha.ComputeHash(memStream.ToArray());

            //sqlconn.Close();
            return System.Text.Encoding.UTF8.GetString(hash);
        }



        public void Dispose()
        {
            if(sqlconn.State != System.Data.ConnectionState.Closed)
                sqlconn.Close();
        }
    }
}
