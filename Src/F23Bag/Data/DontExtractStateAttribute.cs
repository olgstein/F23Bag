﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace F23Bag.Data
{
    [AttributeUsage(AttributeTargets.Property)]
    public class DontExtractStateAttribute : Attribute
    {
    }
}
