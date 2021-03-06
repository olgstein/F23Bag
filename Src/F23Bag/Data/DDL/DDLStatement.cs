﻿using System;
using System.Reflection;
namespace F23Bag.Data.DDL
{
    public class DDLStatement
    {
        public DDLStatement(DDLStatementType statementType, Type elementType)
        {
            StatementType = statementType;
            ElementType = elementType;
        }

        public DDLStatement(DDLStatementType statementType, PropertyInfo property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));

            StatementType = statementType;
            Property = property;
            ElementType = property.ReflectedType;
        }

        public Type ElementType { get; private set; }

        public PropertyInfo Property { get; private set; }

        public DDLStatementType StatementType { get; private set; }
    }
}
