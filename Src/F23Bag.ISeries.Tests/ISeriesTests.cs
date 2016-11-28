﻿using System;
using F23Bag.Data;
using F23Bag.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace F23Bag.ISeries.Tests
{
    [TestClass]
    public class ISeriesTests : DatabaseProviderTests
    {
        private ISQLProvider _provider;

        protected override ISQLProvider Provider
        {
            get
            {
                return _provider ?? (_provider = new ISeriesProvider(System.Configuration.ConfigurationManager.ConnectionStrings["ISeries"].ConnectionString));
            }
        }

        protected override bool EnableTests
        {
            get
            {
                return System.Configuration.ConfigurationManager.ConnectionStrings["ISeries"] != null && !string.IsNullOrEmpty(System.Configuration.ConfigurationManager.ConnectionStrings["ISeries"].ConnectionString);
            }
        }

        [TestInitialize]
        public override void CreateTables()
        {
            if (!EnableTests) return;

            ExecuteNonQuery("CREATE TABLE OBJET1_BIS(ID INTEGER NOT NULL GENERATED ALWAYS AS IDENTITY (START WITH 1 INCREMENT BY 1) PRIMARY KEY, NAME NVARCHAR(100), IDFK_OBJET2 INTEGER)");
            foreach (var sql in Provider.GetDDLTranslator().Translate(new Data.DDL.DDLStatement(Data.DDL.DDLStatementType.CreateTable, typeof(Objet2)), new DefaultSqlMapping(null))) ExecuteNonQuery(sql);
            foreach (var sql in Provider.GetDDLTranslator().Translate(new Data.DDL.DDLStatement(Data.DDL.DDLStatementType.CreateTable, typeof(Objet3)), new DefaultSqlMapping(null))) ExecuteNonQuery(sql);
            foreach (var sql in Provider.GetDDLTranslator().Translate(new Data.DDL.DDLStatement(Data.DDL.DDLStatementType.CreateTable, typeof(Objet1)), new DefaultSqlMapping(null))) ExecuteNonQuery(sql);

            ExecuteNonQuery("INSERT INTO OBJET2(VALUE2) VALUES(1.2)");
            ExecuteNonQuery("INSERT INTO OBJET2(VALUE2) VALUES(2.2)");
            ExecuteNonQuery("INSERT INTO OBJET2(VALUE2) VALUES(3.2)");

            ExecuteNonQuery("INSERT INTO OBJET1(NAME, IDFK_OBJET2) VALUES('obj1', 1)");
            ExecuteNonQuery("INSERT INTO OBJET1(NAME, IDFK_OBJET2) VALUES('obj2', 2)");
            ExecuteNonQuery("INSERT INTO OBJET1(NAME, IDFK_OBJET2) VALUES('obj3', 3)");

            ExecuteNonQuery("INSERT INTO OBJET1_BIS(NAME, IDFK_OBJET2) VALUES('obj1', 1)");
            ExecuteNonQuery("INSERT INTO OBJET1_BIS(NAME, IDFK_OBJET2) VALUES('obj2', 2)");
            ExecuteNonQuery("INSERT INTO OBJET1_BIS(NAME, IDFK_OBJET2) VALUES('obj3', 3)");

            ExecuteNonQuery("INSERT INTO OBJET3(VALUE3, IDFK_OBJET2, IDFK_OBJET3LIST) VALUES(1.3, 3, 1)");
            ExecuteNonQuery("INSERT INTO OBJET3(VALUE3, IDFK_OBJET2, IDFK_OBJET3LIST) VALUES(2.3, 2, 1)");
            ExecuteNonQuery("INSERT INTO OBJET3(VALUE3, IDFK_OBJET2, IDFK_OBJET3LIST) VALUES(3.3, 1, 2)");
        }

        [TestCleanup]
        public override void DropTables()
        {
            if (!EnableTests) return;

            ExecuteNonQuery("DROP TABLE OBJET1");
            ExecuteNonQuery("DROP TABLE OBJET1_BIS");
            ExecuteNonQuery("DROP TABLE OBJET2");
            ExecuteNonQuery("DROP TABLE OBJET3");
        }
    }
}
