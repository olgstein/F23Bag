﻿using F23Bag.Data;
using System;
using System.Data;

namespace F23Bag.ISeries
{
    public class ISeriesProvider : ISQLProvider
    {
        private readonly string _connectionString;

        public ISeriesProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IDbConnection GetConnection()
        {
            return new AdoProxies.ISeriesDbConnection(new IBM.Data.DB2.iSeries.iDB2Connection(_connectionString));
        }

        public IDDLTranslator GetDDLTranslator()
        {
            return new ISeriesDDLTranslator();
        }

        public ISQLTranslator GetSQLTranslator()
        {
            return new ISeriesSQLTranslator();
        }
    }
}
