﻿using F23Bag.Data.DML;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace F23Bag.Data
{
    public interface IPropertyMapper
    {
        bool Accept(PropertyInfo property);

        void DeclareMap(Request request, PropertyInfo property, AliasDefinition alias, bool isNewElement);

        void Map(object o, PropertyInfo property, IDataRecord reader, int readerIndex);
    }
}
