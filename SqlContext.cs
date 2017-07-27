using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace SqlContext
{
    /// <summary>
    ///     Sql上下文
    /// </summary>
    public class SqlContext
    {
        #region 数据属性
        /// <summary>
        ///     Command
        /// </summary>
        public DbCommand Command { get; set; }

        /// <summary>
        ///     Sql内容
        /// </summary>
        public string CommandText { get; set; }

        /// <summary>
        ///     数据库连接
        /// </summary>
        public DbConnection Connection { get; private set; }

        /// <summary>
        ///     事务
        /// </summary>
        public DbTransaction Transaction { get; private set; }

        /// <summary>
        ///     是否需要关闭连接
        /// </summary>
        protected bool NeedClose { get; set; } = false;
        #endregion

        #region 构造函数

        public SqlContext(DbConnection conn, string commandText)
        {
            Connection = conn;
            Command = conn.CreateCommand();
            Command.CommandText = CommandText = commandText;
        }

        public SqlContext(DbTransaction trans, string commandText) : this(trans.Connection, commandText)
        {
            Transaction = trans;
        }
        #endregion

        #region 设置参数

        /// <summary>
        ///     设置参数
        /// </summary>
        public SqlContext Parameter<T>(string name, T value)
        {
            return Parameter(name, GetDbType(typeof(T)), value);
        }

        /// <summary>
        ///     设置参数
        /// </summary>
        public SqlContext Parameter(string name, DbType dbType, object value)
        {
            if (!name.StartsWith("@")) name = "@" + name;
            foreach (DbParameter parameter in Command.Parameters)
            {
                if (parameter.ParameterName != name) continue;

                parameter.DbType = dbType;
                parameter.Value = value;
                return this;
            }

            var dbParameter = Command.CreateParameter();
            Command.Parameters.Add(dbParameter);

            dbParameter.ParameterName = name;
            dbParameter.DbType = dbType;
            dbParameter.Value = value;

            return this;
        }

        /// <summary>
        ///     设置多个参数
        /// </summary>
        public SqlContext Parameters(params object[] parameters)
        {
            /*1.提取所有以@开头的标识符*/
            var parameterNames = new List<string>();
            for (var i = 0; i < CommandText.Length; i++)
            {
                if (CommandText[i] != '@') continue;

                var sb = new StringBuilder();
                for (var j = i + 1; j < CommandText.Length; j++)
                {
                    var c = CommandText[j];
                    if (c == '_' || c == '-' || char.IsLetterOrDigit(c))
                    {
                        sb.Append(c);
                        i++;
                        continue;
                    }
                    break;
                }

                var name = sb.ToString();
                if (!parameterNames.Contains(name))
                {
                    parameterNames.Add(name);
                }
            }

            /*2.已设置过的参数不再重复设置*/
            foreach (DbParameter parameter in Command.Parameters)
            {
                var name = parameter.ParameterName.TrimStart('@');
                parameterNames.Remove(name);
            }

            /*3.按顺序逐个设置参数*/
            var count = Math.Min(parameters.Length, parameterNames.Count);
            for (var i = 0; i < count; i++)
            {
                var name = parameterNames[i];
                if (!name.StartsWith("@")) name = "@" + name;

                var value = parameters[i];

                var dbParameter = Command.CreateParameter();
                Command.Parameters.Add(dbParameter);

                dbParameter.ParameterName = name;
                dbParameter.DbType = GetDbType(value == null ? typeof(string) : value.GetType());
                dbParameter.Value = value;
            }

            return this;
        }
        #endregion

        #region 获取结果

        /// <summary>
        ///     无返回结果
        /// </summary>
        public void NonQuery()
        {
            try
            {
                TryOpen();

                Command.ExecuteNonQuery();
            }
            finally
            {
                TryClose();
            }
        }

        /// <summary>
        ///     返回多行结果
        /// </summary>
        public List<T> Many<T>(Func<DbDataReader, T> selector)
        {
            try
            {
                TryOpen();

                var ls = new List<T>();
                if (selector == null) return ls;

                var reader = Command.ExecuteReader();
                while (reader.Read())
                {
                    var item = selector(reader);
                    if (item != null) ls.Add(item);
                }

                return ls;
            }
            finally
            {
                TryClose();
            }
        }

        /// <summary>
        ///     返回多行结果
        /// </summary>
        public List<T> Many<T>(Action<T> callback = null)
        {
            var selector = GetMapper<T>();
            var ls = Many(selector);

            if (callback != null)
            {
                foreach (var item in ls)
                {
                    callback(item);
                }
            }
            return ls;
        }

        /// <summary>
        ///     返回单行结果
        /// </summary>
        public T Single<T>(Func<DbDataReader, T> selector)
        {
            try
            {
                TryOpen();

                if (selector == null) return default(T);

                var reader = Command.ExecuteReader();
                if (reader.Read())
                {
                    var ret = selector(reader);
                    return ret;
                }

                return default(T);
            }
            finally
            {
                TryClose();
            }
        }

        /// <summary>
        ///     返回单行结果
        /// </summary>
        public T Single<T>(Action<T> callback = null)
        {
            var selector = GetMapper<T>();
            var ret = Single(selector);

            callback?.Invoke(ret);
            return ret;
        }

        /// <summary>
        ///     返回单个值
        /// </summary>
        public T SingleValue<T>(string col = null)
        {
            try
            {
                TryOpen();

                var reader = Command.ExecuteReader();
                if (reader.Read())
                {
                    if (string.IsNullOrEmpty(col))
                    {
                        var ret = reader[0];
                        return (T)ret;
                    }
                    else
                    {
                        var ret = reader[col];
                        return (T)ret;
                    }
                }

                return default(T);
            }
            finally
            {
                TryClose();
            }
        }

        /// <summary>
        ///     如果有必要,则打开连接
        /// </summary>
        protected void TryOpen()
        {
            if (Connection.State == ConnectionState.Closed)
            {
                Connection.Open();
                NeedClose = true;
            }
        }

        /// <summary>
        ///     如果有必要,则关闭连接
        /// </summary>
        protected void TryClose()
        {
            if (NeedClose)
            {
                Connection.Close();
                NeedClose = false;
            }
        }
        #endregion

        #region 类型转换
        private static Dictionary<Type, DbType> typeMap;

        private DbType GetDbType(Type type)
        {
            if (typeMap == null)
            {
                typeMap = new Dictionary<Type, DbType>();

                typeMap[typeof(string)] = DbType.String;
                typeMap[typeof(byte)] = DbType.Byte;
                typeMap[typeof(short)] = DbType.Int16;
                typeMap[typeof(int)] = DbType.Int32;
                typeMap[typeof(long)] = DbType.Int64;
                typeMap[typeof(bool)] = DbType.Boolean;
                typeMap[typeof(DateTime)] = DbType.DateTime2;
                typeMap[typeof(DateTimeOffset)] = DbType.DateTimeOffset;
                typeMap[typeof(decimal)] = DbType.Decimal;
                typeMap[typeof(double)] = DbType.Double;
                typeMap[typeof(TimeSpan)] = DbType.Time;
            }

            return typeMap[type];
        }
        #endregion

        #region 实体映射
        public static readonly Dictionary<Type, Delegate> Mapper = new Dictionary<Type, Delegate>();

        public static void RegistMapper<T>(Func<DbDataReader, T> mapper)
        {
            var type = typeof(T);
            if (Mapper.ContainsKey(type))
            {
                Mapper[type] = mapper;
            }
            else
            {
                Mapper.Add(type, mapper);
            }
        }

        public static T Map<T>(DbDataReader reader)
        {
            var mapper = GetMapper<T>();
            if (mapper == null) return default(T);

            return mapper(reader);
        }

        public static Func<DbDataReader, T> GetMapper<T>()
        {
            var type = typeof(T);
            if (!Mapper.ContainsKey(type)) return null;

            var mapper = Mapper[type] as Func<DbDataReader, T>;
            return mapper;
        }
        #endregion

        #region 事务操作

        /// <summary>
        ///     启用事务
        /// </summary>
        public DbTransaction BeginTransaction()
        {
            return Transaction = Connection.BeginTransaction();
        }

        /// <summary>
        ///     提交事务
        /// </summary>
        public void Commit()
        {
            if (Transaction != null)
            {
                Transaction.Commit();
                Transaction = null;
            }
        }
        #endregion
    }

    /// <summary>
    ///     扩展方法
    /// </summary>
    public static class DbConnectionExtension
    {
        public static SqlContext Sql(this DbConnection conn, string sql, params object[] parameters)
        {
            var context = new SqlContext(conn, sql);
            context.Parameters(parameters);
            return context;
        }

        public static SqlContext Sql(this DbTransaction trans, string sql, params object[] parameters)
        {
            var context = new SqlContext(trans, sql);
            context.Parameters(parameters);
            return context;
        }

        /// <summary>
        ///     创建表
        /// </summary>
        public static SqlContext CreateTable(this DbConnection conn, string tableName,string columnDefinetion)
        {
            var sql = $"CREATE TABLE IF NOT EXISTS {tableName}({columnDefinetion});";
            return Sql(conn, sql);
        }

        /// <summary>
        ///     Select语句
        /// </summary>
        public static SqlContext Select(this DbConnection conn, string tableName)
        {
            var sql = $"SELECT * FROM {tableName}";

            return Sql(conn, sql);
        }

        /// <summary>
        ///     Select语句
        /// </summary>
        public static SqlContext Select(this DbConnection conn, string tableName, string where, params object[] parameters)
        {
            var sql = $"SELECT * FROM {tableName}";
            if (!string.IsNullOrEmpty(where)) sql += " WHERE " + where;

            return Sql(conn, sql, parameters);
        }

        /// <summary>
        ///     Count语句
        /// </summary>
        public static SqlContext Count(this DbConnection conn, string tableName)
        {
            var sql = $"SELECT COUNT(*) FROM {tableName}";
            return Sql(conn, sql);
        }

        /// <summary>
        ///     Count语句
        /// </summary>
        public static SqlContext Count(this DbConnection conn, string tableName, string where,
            params object[] parameters)
        {
            var sql = $"SELECT COUNT(*) FROM {tableName}";
            if (!string.IsNullOrEmpty(where)) sql += " WHERE " + where;

            return Sql(conn, sql, parameters);
        }

        /// <summary>
        ///     Insert语句
        /// </summary>
        public static SqlContext Insert(this DbConnection conn, string tableName, string columns, params object[] parameters)
        {
            var sb = new StringBuilder();
            sb.Append("INSERT INTO ");
            sb.Append(tableName);
            sb.Append("(");
            sb.Append(columns);
            sb.Append(") VALUES(");

            var names = columns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var values = string.Join(",", names.Select(i => "@" + i.Trim()).ToArray());
            sb.Append(values);

            sb.Append(")");

            return Sql(conn, sb.ToString(), parameters);
        }

        /// <summary>
        ///     Update语句
        /// </summary>
        public static SqlContext UpdateSql(this DbConnection conn, string tableName, string columns, string where)
        {
            return UpdateSql(conn, tableName, columns, where, new object[] { });
        }

        /// <summary>
        ///     Update语句
        /// </summary>
        public static SqlContext UpdateSql(this DbConnection conn, string tableName, string columns, string where, params object[] parameters)
        {
            var sb = new StringBuilder();
            sb.Append($"UPDATE {tableName} ");

            if (!string.IsNullOrEmpty(columns))
            {
                sb.Append("SET ");
                var names = columns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                var str = string.Join(",", names.Select(i => i + " = @" + i).ToArray());
                sb.Append(str);
            }

            if (!string.IsNullOrEmpty(where))
            {
                sb.Append(" WHERE " + where);
            }

            return Sql(conn, sb.ToString(), parameters);
        }

        /// <summary>
        ///     Delete语句
        /// </summary>
        public static SqlContext Delete(this DbConnection conn, string tableName, string where, params object[] parameters)
        {
            var sql = $"DELETE FROM {tableName}";
            if (!string.IsNullOrEmpty(where)) sql += " WHERE " + where;

            return Sql(conn, sql, parameters);
        }
    }
}
