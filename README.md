# 一、概述

SqlContext是一个轻量级的Sql帮助类，旨在提供一种快速、简洁、优雅地数据库访问方法。

# 二、使用方法

本示例程序使用SQLite数据库，需要先安装SQLite的支持库。

```csharp
class Program
{
    static void Main(string[] args)
    {
        /*0.创建连接*/
        var conn = new SQLiteConnection("Data Source=data.db;");

        /*1.创建表*/
        conn.CreateTable("user", "id integer primary key autoincrement,name varchar(32),password varchar(32)").NonQuery();

        /*2.插入数据,无返回值*/
        conn.Insert("user", "name,password", "test", "123456").NonQuery();

        /*3.查询多行数据*/
        var userList = conn.Select("user").Many(r => new User
        {
            Id = (long)r["id"],
            Name = (string)r["name"],
            Password = (string)r["password"]
        });

        /*4.注册Mapper函数*/
        SqlContext.RegistMapper(r => new User
        {
            Id = (long)r["id"],
            Name = (string)r["name"],
            Password = (string)r["password"]
        });

        /*5.查询多行数据(使用mapper)*/
        userList = conn.Select("user").Many<User>();

        /*6.查询单行数据(使用mapper)*/
        var user = conn.Select("user").Single<User>();

        /*7.传递参数(方式1)*/
        userList = conn.Select("user", "id>@min and id<@max", 1, 3).Many<User>();

        /*8.传递参数(方式2)*/
        userList = conn.Select("user", "id>@min and id<@max")
            .Parameters(1, 3)
            .Many<User>();

        /*9.传递参数(方式3)*/
        userList = conn.Select("user", "id>@min and id<@max")
            .Parameter("min", 1)
            .Parameter("max", DbType.Int32, 3)
            .Many<User>();

        /*10.执行任意sql(获取单个值)*/
        var count = conn.Sql("select count(*) from user").SingleValue<long>();
    }

    public class User
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
    }
}
```
