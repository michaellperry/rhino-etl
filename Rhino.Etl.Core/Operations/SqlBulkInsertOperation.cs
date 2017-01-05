using System.Configuration;
using Rhino.Etl.Core.Infrastructure;

namespace Rhino.Etl.Core.Operations
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.SqlClient;
    using DataReaders;

    /// <summary>
    /// Allows to execute an operation that perform a bulk insert into a sql server database
    /// </summary>
    public abstract class SqlBulkInsertOperation : AbstractDatabaseOperation
    {
        /// <summary>
        /// The schema of the destination table
        /// </summary>
        private IDictionary<string, Type> _schema = new Dictionary<string, Type>();

        /// <summary>
        /// The mapping of columns from the row to the database schema.
        /// Important: The column name in the database is case sensitive!
        /// </summary>
        public IDictionary<string, string> Mappings = new Dictionary<string, string>();
        private readonly IDictionary<string, Type> _inputSchema = new Dictionary<string, Type>();

        private SqlBulkCopy sqlBulkCopy;
        private string targetTable;
        private int timeout;
        private int batchSize;
        private    int    notifyBatchSize;
        private SqlBulkCopyOptions bulkCopyOptions = SqlBulkCopyOptions.Default;
        

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlBulkInsertOperation"/> class.
        /// </summary>
        /// <param name="connectionStringName">Name of the connection string.</param>
        /// <param name="targetTable">The target table.</param>
        protected SqlBulkInsertOperation(string connectionStringName, string targetTable)
            : this(ConfigurationManager.ConnectionStrings[connectionStringName], targetTable)
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlBulkInsertOperation"/> class.
        /// </summary>
        /// <param name="connectionStringSettings">Connection string settings to use.</param>
        /// <param name="targetTable">The target table.</param>
        protected SqlBulkInsertOperation(ConnectionStringSettings connectionStringSettings, string targetTable)
            : this(connectionStringSettings, targetTable, 600)
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlBulkInsertOperation"/> class.
        /// </summary>
        /// <param name="connectionStringName">Name of the connection string.</param>
        /// <param name="targetTable">The target table.</param>
        /// <param name="timeout">The timeout.</param>
        protected SqlBulkInsertOperation(string connectionStringName, string targetTable, int timeout)
            : this(ConfigurationManager.ConnectionStrings[connectionStringName], targetTable, timeout)
        {
            Guard.Against(string.IsNullOrEmpty(targetTable), "TargetTable was not set, but it is mandatory");
            this.targetTable = targetTable;
            this.timeout = timeout;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlBulkInsertOperation"/> class.
        /// </summary>
        /// <param name="connectionStringSettings">Connection string settings to use.</param>
        /// <param name="targetTable">The target table.</param>
        /// <param name="timeout">The timeout.</param>
        protected SqlBulkInsertOperation(ConnectionStringSettings connectionStringSettings, string targetTable, int timeout)
            : base(connectionStringSettings)
        {
            Guard.Against(string.IsNullOrEmpty(targetTable), "TargetTable was not set, but it is mandatory");
            this.targetTable = targetTable;
            this.timeout = timeout;
        }

        /// <summary>The timeout value of the bulk insert operation</summary>
        public virtual int Timeout
        {
            get { return timeout; }
            set { timeout = value; }
        }

        /// <summary>The batch size value of the bulk insert operation</summary>
        public virtual int BatchSize
        {
            get { return batchSize; }
            set { batchSize = value; }
        }

        ///    <summary>The batch size    value of the bulk insert operation</summary>
        public virtual int NotifyBatchSize
        {
            get    { return notifyBatchSize>0 ? notifyBatchSize : batchSize; }
            set    { notifyBatchSize =    value; }
        }

        /// <summary>The table or view to bulk load the data into.</summary>
        public string TargetTable
        {
            get { return targetTable; }
            set { targetTable = value; }
        }

        /// <summary><c>true</c> to turn the <see cref="SqlBulkCopyOptions.TableLock"/> option on, otherwise <c>false</c>.</summary>
        public virtual bool LockTable
        {
            get { return IsOptionOn(SqlBulkCopyOptions.TableLock); }
            set { ToggleOption(SqlBulkCopyOptions.TableLock, value); }
        }

        /// <summary><c>true</c> to turn the <see cref="SqlBulkCopyOptions.KeepIdentity"/> option on, otherwise <c>false</c>.</summary>
        public virtual bool KeepIdentity
        {
            get { return IsOptionOn(SqlBulkCopyOptions.KeepIdentity); }
            set { ToggleOption(SqlBulkCopyOptions.KeepIdentity, value); }
        }

        /// <summary><c>true</c> to turn the <see cref="SqlBulkCopyOptions.KeepNulls"/> option on, otherwise <c>false</c>.</summary>
        public virtual bool KeepNulls
        {
            get { return IsOptionOn(SqlBulkCopyOptions.KeepNulls); }
            set { ToggleOption(SqlBulkCopyOptions.KeepNulls, value); }
        }

        /// <summary><c>true</c> to turn the <see cref="SqlBulkCopyOptions.CheckConstraints"/> option on, otherwise <c>false</c>.</summary>
        public virtual bool CheckConstraints
        {
            get { return IsOptionOn(SqlBulkCopyOptions.CheckConstraints); }
            set { ToggleOption(SqlBulkCopyOptions.CheckConstraints, value); }
        }

        /// <summary><c>true</c> to turn the <see cref="SqlBulkCopyOptions.FireTriggers"/> option on, otherwise <c>false</c>.</summary>
        public virtual bool FireTriggers
        {
            get { return IsOptionOn(SqlBulkCopyOptions.FireTriggers); }
            set { ToggleOption(SqlBulkCopyOptions.FireTriggers, value); }
        }

        /// <summary>Turns a <see cref="bulkCopyOptions"/> on or off depending on the value of <paramref name="on"/></summary>
        /// <param name="option">The <see cref="SqlBulkCopyOptions"/> to turn on or off.</param>
        /// <param name="on"><c>true</c> to set the <see cref="SqlBulkCopyOptions"/> <paramref name="option"/> on otherwise <c>false</c> to turn the <paramref name="option"/> off.</param>
        protected void ToggleOption(SqlBulkCopyOptions option, bool on)
        {
            if (on)
            {
                TurnOptionOn(option);
            }
            else
            {
                TurnOptionOff(option);
            }
        }

        /// <summary>Returns <c>true</c> if the <paramref name="option"/> is turned on, otherwise <c>false</c></summary>
        /// <param name="option">The <see cref="SqlBulkCopyOptions"/> option to test for.</param>
        /// <returns></returns>
        protected bool IsOptionOn(SqlBulkCopyOptions option)
        {
            return (bulkCopyOptions & option) == option;
        }

        /// <summary>Turns the <paramref name="option"/> on.</summary>
        /// <param name="option"></param>
        protected void TurnOptionOn(SqlBulkCopyOptions option)
        {
            bulkCopyOptions |= option;
        }

        /// <summary>Turns the <paramref name="option"/> off.</summary>
        /// <param name="option"></param>
        protected void TurnOptionOff(SqlBulkCopyOptions option)
        {
            if (IsOptionOn(option))
                bulkCopyOptions ^= option;
        }

        /// <summary>The table or view's schema information.</summary>
        public IDictionary<string, Type> Schema
        {
            get { return _schema; }
            set { _schema = value; }
        }

        /// <summary>
        /// Prepares the mapping for use, by default, it uses the schema mapping.
        /// This is the preferred appraoch
        /// </summary>
        public virtual void PrepareMapping()
        {
            foreach (KeyValuePair<string, Type> pair in _schema)
            {
                Mappings[pair.Key] = pair.Key;
            }
        }

        /// <summary>Use the destination Schema and Mappings to create the
        /// operations input schema so it can build the adapter for sending
        /// to the WriteToServer method.</summary>
        public virtual void CreateInputSchema()
        {
            foreach(KeyValuePair<string, string> pair in Mappings)
            {
                _inputSchema.Add(pair.Key, _schema[pair.Value]);
            }
        }

        /// <summary>
        /// Executes this operation
        /// </summary>
        public override IEnumerable<Row> Execute(IEnumerable<Row> rows)
        {
            Guard.Against<ArgumentException>(rows == null, "SqlBulkInsertOperation cannot accept a null enumerator");
            PrepareSchema();
            PrepareMapping();
            CreateInputSchema();
            using (SqlConnection connection = (SqlConnection)Use.Connection(ConnectionStringSettings))
            using (SqlTransaction transaction = (SqlTransaction) BeginTransaction(connection))
            {
                sqlBulkCopy = CreateSqlBulkCopy(connection, transaction);
                DictionaryEnumeratorDataReader adapter = new DictionaryEnumeratorDataReader(_inputSchema, rows);
                try
                {
                    sqlBulkCopy.WriteToServer(adapter);
                }
                catch (InvalidOperationException)
                {
                    CompareSqlColumns(connection, transaction, rows);
                    throw;
                }

                if (PipelineExecuter.HasErrors)
                {
                    Warn("Rolling back transaction in {0}", Name);
                    if (transaction != null) transaction.Rollback();
                    Warn("Rolled back transaction in {0}", Name);
                }
                else
                {
                    Debug("Committing {0}", Name);
                    if (transaction != null) transaction.Commit();
                    Debug("Committed {0}", Name);
                }
            }
            yield break;
        }

        /// <summary>
        ///    Handle sql notifications
        ///    </summary>
        protected virtual void onSqlRowsCopied(object sender, SqlRowsCopiedEventArgs e)
        {
            Debug("{0} rows    copied to database", e.RowsCopied);
        }

        ///    <summary>
        /// Prepares the schema of the target table
        /// </summary>
        protected abstract void PrepareSchema();

        /// <summary>
        /// Creates the SQL bulk copy instance
        /// </summary>
        private SqlBulkCopy CreateSqlBulkCopy(SqlConnection connection, SqlTransaction transaction)
        {
            SqlBulkCopy copy = new SqlBulkCopy(connection, bulkCopyOptions, transaction);
            copy.BatchSize = batchSize;
            foreach (KeyValuePair<string, string> pair in Mappings)
            {
                copy.ColumnMappings.Add(pair.Key, pair.Value);
            }
            copy.NotifyAfter = NotifyBatchSize;
            copy.SqlRowsCopied += onSqlRowsCopied;
            copy.DestinationTableName = TargetTable;
            copy.BulkCopyTimeout = Timeout;
            return copy;
        }

        private void CompareSqlColumns(SqlConnection connection, SqlTransaction transaction, IEnumerable<Row> rows)
        {
            var command = connection.CreateCommand();
            command.CommandText = "select * from {TargetTable} where 1=0".Replace("{TargetTable}", TargetTable);
            command.CommandType = CommandType.Text;
            command.Transaction = transaction;

            using (var reader = command.ExecuteReader(CommandBehavior.KeyInfo))
            {
                var schemaTable = reader.GetSchemaTable();
                var databaseColumns = schemaTable.Rows
                    .OfType<DataRow>()
                    .Select(r => new
                    {
                        Name = (string)r["ColumnName"],
                        Type = (Type)r["DataType"],
                        IsNullable = (bool)r["AllowDBNull"],
                        MaxLength = (int)r["ColumnSize"]
                    })
                    .ToArray();

                var missingColumns = _schema.Keys.Except(
                    databaseColumns.Select(c => c.Name));
                if (missingColumns.Any())
                    throw new InvalidOperationException(
                        "The following columns are not in the target table: " +
                        string.Join(", ", missingColumns.ToArray()));
                var differentColumns = _schema
                    .Select(s => new
                    {
                        Name = s.Key,
                        SchemaType = s.Value,
                        DatabaseType = databaseColumns.Single(c => c.Name == s.Key)
                    })
                    .Where(c => !TypesMatch(c.SchemaType, c.DatabaseType.Type, c.DatabaseType.IsNullable));
                if (differentColumns.Any())
                    throw new InvalidOperationException(
                        "The following columns have different types in the target table: " +
                        string.Join(", ", differentColumns
                            //.Select(c => $"{c.Name}: is {GetFriendlyName(c.SchemaType)}, but should be {GetFriendlyName(c.DatabaseType.Type)}{(c.DatabaseType.IsNullable ? "?" : "")}.")
                            // c.Name, GetFriendlyName(c.SchemaType), GetFriendlyName(c.DatabaseType.Type), (c.DatabaseType.IsNullable ? \"?\" : \"\")
                            .Select(c => string.Format("{0}: is {1}, but should be {2}{3}.", c.Name,
                                GetFriendlyName(c.SchemaType), GetFriendlyName(c.DatabaseType.Type),
                                (c.DatabaseType.IsNullable ? "?" : "")))
                            .ToArray()
                            ));
                var stringsTooLong =
                    (from column in databaseColumns
                     where column.Type == typeof(string)
                     from mapping in Mappings
                     where mapping.Value == column.Name
                     let name = mapping.Key
                     from row in rows
                     let value = (string)row[name]
                     where value != null && value.Length > column.MaxLength
                     select new { column.Name, column.MaxLength, Value = value })
                    .ToArray();
                if (stringsTooLong.Any())
                    throw new InvalidOperationException(
                        "The folowing columns have values too long for the target table: " +
                        string.Join(", ", stringsTooLong
                            .Select(s => "{s.Name}: max length is {s.MaxLength}, value is {s.Value}."
                                .Replace("{s.Name}", s.Name)
                                .Replace("{s.MaxLength}", s.MaxLength.ToString())
                                .Replace("{s.Value}", s.Value)
                            )
                            .ToArray()));
            }
        }

        private static string GetFriendlyName(Type type)
        {
            var friendlyName = type.Name;
            if (!type.IsGenericType)
                return friendlyName;

            var iBacktick = friendlyName.IndexOf('`');
            if (iBacktick > 0)
                friendlyName = friendlyName.Remove(iBacktick);

            var genericParameters = type.GetGenericArguments()
                .Select(x => GetFriendlyName(x))
                .ToArray();
            friendlyName += "<" + string.Join(", ", genericParameters) + ">";

            return friendlyName;
        }

        private bool TypesMatch(Type schemaType, Type databaseType, bool isNullable)
        {
            if (schemaType == databaseType)
                return true;
            if (isNullable && schemaType == typeof(Nullable<>).MakeGenericType(databaseType))
                return true;
            return false;
        }
    }
}