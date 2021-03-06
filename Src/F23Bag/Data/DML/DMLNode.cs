﻿namespace F23Bag.Data.DML
{
    public abstract class DMLNode
    {
        public DMLNode Parent { get; set; }

        public abstract void Accept(IDMLAstVisitor visitor);

        public Request Request
        {
            get
            {
                if (Parent is Request) return (Request)Parent;
                if (Parent != null) return Parent.Request;
                return this as Request;
            }
        }

        internal abstract DMLNode Clone(AliasDefinition source, AliasDefinition replace);
    }
}
