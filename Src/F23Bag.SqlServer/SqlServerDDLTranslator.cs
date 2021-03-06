﻿using F23Bag.Data;
using System;
using System.Text;
using F23Bag.Data.DML;
using System.Reflection;

namespace F23Bag.SqlServer
{
    internal class SqlServerDDLTranslator : DDLTranslatorBase
    {
        protected override StringBuilder GetColumnDefinition(ISQLMapping sqlMapping, PropertyInfo property, out bool isAlter)
        {
            var sql = new StringBuilder();
            isAlter = false;

            var columnName = sqlMapping.GetColumnName(property);
            if (sqlMapping.GetIdProperty(property.DeclaringType).Name.Equals(property.Name) && property.PropertyType == typeof(int))
                sql.Append(columnName).Append(" INTEGER NOT NULL IDENTITY PRIMARY KEY");
            else if (property.PropertyType.IsEntityOrCollection())
            {
                if (property.PropertyType.IsCollection())
                {
                    var idProperty = sqlMapping.GetIdProperty(property.DeclaringType);
                    sql.Append("ALTER TABLE ")
                        .Append(((Identifier)sqlMapping.GetSqlEquivalent(property.PropertyType.GetGenericArguments()[0])).IdentifierName)
                        .Append(" ADD ")
                        .Append(columnName)
                        .Append(' ')
                        .Append(GetSqlTypeName(idProperty.PropertyType))
                        .Append(" REFERENCES ").Append(((Identifier)sqlMapping.GetSqlEquivalent(property.DeclaringType)).IdentifierName).Append('(').Append(sqlMapping.GetColumnName(idProperty)).Append(')');

                    isAlter = true;
                }
                else
                    sql.Append(columnName).Append(' ').Append(GetSqlTypeName(sqlMapping.GetIdProperty(property.PropertyType).PropertyType))
                        .Append(" REFERENCES ").Append(((Identifier)sqlMapping.GetSqlEquivalent(property.PropertyType)).IdentifierName).Append('(').Append(sqlMapping.GetColumnName(property.PropertyType.GetProperty("Id"))).Append(')');
            }
            else
            {
                var isNullable = property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>);
                var sqlTypeName = GetSqlTypeName(property.PropertyType);
                sql.Append(columnName).Append(' ').Append(sqlTypeName);
                if (!isNullable) sql.Append(" NOT NULL");
            }

            return sql;
        }

        protected override string GetSqlTypeName(Type type)
        {
            if (type == typeof(DateTime) || type == typeof(DateTime?))
                return "DATETIME2";
            else
                return base.GetSqlTypeName(type);
        }
    }
}
