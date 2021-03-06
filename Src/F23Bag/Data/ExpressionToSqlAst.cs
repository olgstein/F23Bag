﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using F23Bag.Data.DML;
using F23Bag.Data.Mapping;

namespace F23Bag.Data
{
    internal class ExpressionToSqlAst : ExpressionVisitor
    {
        private readonly ISQLMapping _sqlMapping;
        private readonly IEnumerable<IExpresstionToSqlAst> _customConverters;
        private readonly Mapper _mapper;
        private readonly Dictionary<PropertyInfo, PropertyInfo> _equivalentProperties;
        private DMLNode _san;
        private DMLNode SqlAstNode
        {
            get { return _san; }
            set
            {
                _san = value;
            }
        }
        private Request _request;
        private bool _inOr;

        internal ExpressionToSqlAst(ISQLMapping sqlMapping, IEnumerable<IExpresstionToSqlAst> customConverters, Mapper mapper)
        {
            _sqlMapping = sqlMapping;
            _customConverters = customConverters ?? new IExpresstionToSqlAst[] { };
            _mapper = mapper;
            _equivalentProperties = new Dictionary<PropertyInfo, PropertyInfo>();
        }

        internal Request Translate(Expression expression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            Visit(expression);

            return _mapper.DeclareMap(_request.TopParentRequest, expression);
        }

        public static Expression StripQuotes(Expression e)
        {
            while (e.NodeType == ExpressionType.Quote) e = ((System.Linq.Expressions.UnaryExpression)e).Operand;
            return e;
        }

        public override Expression Visit(Expression node)
        {
            var converter = _customConverters.FirstOrDefault(c => c.Accept(node));
            if (converter != null)
            {
                SqlAstNode = converter.Convert(node, _request, _sqlMapping);
                return node;
            }

            return base.Visit(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression m)
        {
            if (m.Method.ReflectedType == typeof(string) && m.Method.GetParameters().Length == 1) return VisitStringMethodCall(m);
            if (m.Method.ReflectedType == typeof(QueryableExtension)) return VisitQueryableExtensionMethodCall(m);
            if (m.Method.ReflectedType == typeof(UnitOfWork)) return VisiteUnitOfWorkMethodCall(m);
            if (m.Method.ReflectedType == typeof(Utilities)) return VisitUtilitiesMethodCall(m);

            if (m.Arguments.Count > 0) Visit(m.Arguments[0]);

            if ((m.Method.ReflectedType == typeof(Queryable) || m.Method.ReflectedType == typeof(Enumerable)) && m.Method.Name == "Skip")
            {
                _request.Skip = (int)((ConstantExpression)m.Arguments[1]).Value;
                return m;
            }
            else if ((m.Method.ReflectedType == typeof(Queryable) || m.Method.ReflectedType == typeof(Enumerable)) && m.Method.Name == "Take")
            {
                _request.Take = (int)((ConstantExpression)m.Arguments[1]).Value;
                return m;
            }

            if (m.Method.ReflectedType == typeof(Queryable)) return VisitQueryableMethodCall(m);
            if (m.Method.ReflectedType == typeof(Enumerable)) return VisitEnumerableMethodCall(m);

            if (m.Method.ReflectedType.IsGenericType && m.Method.ReflectedType.GetGenericTypeDefinition() == typeof(List<>) && m.Method.Name == "Contains")
            {
                Visit(m.Arguments[0]);

                var cstExp = m.Object as ConstantExpression;
                if (cstExp == null) throw new NotSupportedException("Contains supported only on constant (IEnumerable or List<>) : " + m.ToString());

                SqlAstNode = new In(SqlAstNode, ((System.Collections.IEnumerable)cstExp.Value).OfType<object>().Where(o => o != null).Select(o => new Constant(o, _sqlMapping)).ToArray());
                return m;
            }

            throw new NotSupportedException($"The method '{m.Method.Name}' is not supported : {m.ToString()}");
        }

        private Expression VisitUtilitiesMethodCall(MethodCallExpression m)
        {
            if (m.Method.Name == nameof(Utilities.UseLeftJoin))
            {
                var oldInOr = _inOr;
                _inOr = true;

                Visit(m.Arguments[0]);

                _inOr = oldInOr;
            }

            return m;
        }

        private Expression VisiteUnitOfWorkMethodCall(MethodCallExpression m)
        {
            Visit(m.Arguments[0]);

            if (m.Method.Name == "Delete")
            {
                _request.RequestType = RequestType.Delete;
            }
            else if (m.Method.Name == "Update" || m.Method.Name == "Insert")
            {
                if (m.Method.Name == "Insert")
                    _request.RequestType = RequestType.InsertSelect;
                else
                    _request.RequestType = RequestType.Update;

                var upOrInExp = (LambdaExpression)StripQuotes(m.Arguments[1]);
                _request.FromAlias.Equivalents.Add(upOrInExp.Parameters[0]);
                var newExp = upOrInExp.Body as MemberInitExpression;
                if (newExp == null) throw new NotSupportedException("Insert or Update not supported : " + m.ToString());

                foreach (var binding in newExp.Bindings)
                {
                    var mba = binding as MemberAssignment;
                    if (mba == null) throw new NotSupportedException("Insert or Update not supported : " + mba.ToString());

                    Visit(mba.Expression);
                    _request.UpdateOrInsert.Add(new UpdateOrInsertInfo(SqlAstNode, (ColumnAccess)_sqlMapping.GetSqlEquivalent(_request, _request.FromAlias, GetRealProperty((PropertyInfo)mba.Member), false)));
                }
            }
            else
                throw new NotSupportedException($"The method '{m.Method.Name}' is not supported : {m.ToString()}");

            return m;
        }

        private Expression VisitEnumerableMethodCall(MethodCallExpression m)
        {
            if ((m.Method.Name == "Where" || (m.Method.Name.StartsWith("First", StringComparison.Ordinal) && m.Method.GetParameters().Length == 2) || (m.Method.Name == "Any" && m.Method.GetParameters().Length == 2)))
            {
                var alias = (AliasDefinition)SqlAstNode;
                var filter = (LambdaExpression)StripQuotes(m.Arguments[1]);
                alias.Equivalents.Add(filter.Parameters[0]);
                Visit(filter.Body);

                if (m.Method.Name.StartsWith("First", StringComparison.Ordinal))
                {
                    var subRequest = _request.ExtractJoinToSubRequest(alias);
                    subRequest.Take = 1;
                    SqlAstNode = subRequest;
                }
            }
            else if (m.Method.Name == "Contains")
            {
                Visit(m.Arguments[1]);

                var cstExp = m.Arguments[0] as ConstantExpression;
                if (cstExp == null) throw new NotSupportedException("Contains supported only on constant (IEnumerable or List<>) : " + m.ToString());

                SqlAstNode = new In(SqlAstNode, ((System.Collections.IEnumerable)cstExp.Value).OfType<object>().Where(o => o != null).Select(o => new Constant(o, _sqlMapping)).ToArray());
            }
            else if (m.Method.Name.StartsWith("First", StringComparison.Ordinal) && m.Method.GetParameters().Length == 1)
            {
                var alias = (AliasDefinition)SqlAstNode;
                var subRequest = _request.ExtractJoinToSubRequest(alias);
                subRequest.Take = 1;
                SqlAstNode = subRequest;
            }
            else
            {
                var oldRequest = _request;
                var alias = (AliasDefinition)SqlAstNode;
                Action<DMLNode> addAggregation = null;

                if (m.Arguments[0].Type.IsGenericType && typeof(IGrouping<,>).IsAssignableFrom(m.Arguments[0].Type.GetGenericTypeDefinition()))
                    addAggregation = nd => SqlAstNode = nd;
                else
                {
                    var subRequest = _request.ExtractJoinToSubRequest(alias);
                    SqlAstNode = _request = subRequest;
                    addAggregation = nd =>
                    {
                        subRequest.Select.Clear();
                        subRequest.Select.Add(new SelectInfo(nd, null, true));
                        SqlAstNode = subRequest;
                    };
                }

                if (m.Method.Name == "Count" && m.Method.GetParameters().Length == 1)
                    addAggregation(new DML.UnaryExpression(UnaryExpressionTypeEnum.Count, null));
                else if (m.Method.Name == "Max" && m.Method.GetParameters().Length == 2)
                {
                    var selector = (LambdaExpression)StripQuotes(m.Arguments[1]);
                    alias.Equivalents.Add(selector.Parameters[0]);
                    Visit(selector.Body);

                    addAggregation(new DML.UnaryExpression(UnaryExpressionTypeEnum.Max, SqlAstNode));
                }
                else if (m.Method.Name == "Min" && m.Method.GetParameters().Length == 2)
                {
                    var selector = (LambdaExpression)StripQuotes(m.Arguments[1]);
                    alias.Equivalents.Add(selector.Parameters[0]);
                    Visit(selector.Body);

                    addAggregation(new DML.UnaryExpression(UnaryExpressionTypeEnum.Min, SqlAstNode));
                }
                else if (m.Method.Name == "Sum" && m.Method.GetParameters().Length == 2)
                {
                    var selector = (LambdaExpression)StripQuotes(m.Arguments[1]);
                    alias.Equivalents.Add(selector.Parameters[0]);
                    Visit(selector.Body);

                    addAggregation(new DML.UnaryExpression(UnaryExpressionTypeEnum.Sum, SqlAstNode));
                }
                else if (m.Method.Name == "Average" && m.Method.GetParameters().Length == 2)
                {
                    var selector = (LambdaExpression)StripQuotes(m.Arguments[1]);
                    alias.Equivalents.Add(selector.Parameters[0]);
                    Visit(selector.Body);

                    addAggregation(new DML.UnaryExpression(UnaryExpressionTypeEnum.Average, SqlAstNode));
                }
                else
                    throw new NotSupportedException($"The method '{m.Method.Name}' is not supported : {m.ToString()}");

                _request = oldRequest;
            }

            return m;
        }

        private Expression VisitQueryableMethodCall(MethodCallExpression m)
        {
            if (m.Method.Name == "Where" || (m.Method.Name.StartsWith("First", StringComparison.Ordinal) && m.Method.GetParameters().Length == 2))
                VisitQueryableWhereOrFirst(m);
            else if (m.Method.Name == "Distinct" && m.Method.GetParameters().Length == 1)
                _request.HasDistinct = true;
            else if (m.Method.Name == "Select" || m.Method.Name == "SelectMany")
            {
                var select = (LambdaExpression)StripQuotes(m.Arguments[1]);
                _request.FromAlias.Equivalents.Add(select.Parameters[0]);

                var oldInOr = _inOr;
                _inOr = true;

                if (select.Body is MemberExpression && ((MemberExpression)select.Body).Member is PropertyInfo)
                {
                    var memberExp = (MemberExpression)select.Body;
                    if (!(memberExp.Expression is ParameterExpression)) throw new NotSupportedException("Not supported select : " + memberExp.ToString());

                    var property = (PropertyInfo)((MemberExpression)select.Body).Member;
                    var isCollection = property.PropertyType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType);
                    if (m.Method.Name == "Select" && isCollection)
                        throw new SQLException("Must use SelectMany for " + select.ToString());
                    _request.Select.Add(new SelectInfo(_sqlMapping.GetSqlEquivalent(_request, _request.GetAliasFor(select.Parameters[0]), GetRealProperty(property), _inOr), null, _request.Select.Count == 0));

                    if (isCollection)
                        _request.ProjectionType = property.PropertyType.GetGenericArguments()[0];
                    else
                        _request.ProjectionType = property.PropertyType;
                }
                else if (select.Body is NewExpression)
                {
                    var newExp = (NewExpression)select.Body;
                    for (var i = 0; i < newExp.Arguments.Count; i++) VisitNewExpressionArgument(newExp, i, newExp.Arguments[i]);
                    _request.ProjectionType = newExp.Type;
                }
                else if (select.Body is MemberInitExpression)
                {
                    var mbInit = (MemberInitExpression)select.Body;
                    var newElement = true;
                    foreach (var binding in mbInit.Bindings)
                    {
                        if (!(binding is MemberAssignment)) throw new NotSupportedException("Only assignment are supported : " + binding.ToString());
                        var mbAssign = (MemberAssignment)binding;

                        if (mbAssign.Expression is MemberExpression)
                        {
                            var mbAccess = (MemberExpression)mbAssign.Expression;
                            AddEquivalentProperty((PropertyInfo)mbAccess.Member, (PropertyInfo)mbAssign.Member);
                            var alias = _request.FromAlias;

                            if (mbAccess.Expression is MemberExpression)
                            {
                                var expressions = new Stack<MemberExpression>();
                                var expression = (MemberExpression)mbAccess.Expression;

                                while (expression != null)
                                {
                                    expressions.Push(expression);
                                    expression = expression.Expression as MemberExpression;
                                }

                                while (expressions.Count > 0)
                                {
                                    expression = expressions.Pop();
                                    alias = (AliasDefinition)_sqlMapping.GetSqlEquivalent(_request, alias, GetRealProperty((PropertyInfo)expression.Member), _inOr);
                                }
                            }

                            _request.Select.Add(new SelectInfo(_sqlMapping.GetSqlEquivalent(_request, alias, GetRealProperty((PropertyInfo)mbAccess.Member), _inOr), (PropertyInfo)mbAssign.Member, newElement));
                        }
                        else if (mbAssign.Expression is MethodCallExpression && ((MethodCallExpression)mbAssign.Expression).Method.ReflectedType.Equals(typeof(Queryable)))
                        {
                            Visit(mbAssign.Expression);
                            var subRequest = _request;
                            _request = _request.ParentRequest;
                            _request.Select.Add(new SelectInfo(subRequest, (PropertyInfo)mbAssign.Member, newElement));
                            _sqlMapping.SetSqlEquivalent((PropertyInfo)mbAssign.Member, SqlAstNode);
                        }
                        else
                        {
                            Visit(mbAssign.Expression);
                            _request.Select.Add(new SelectInfo(SqlAstNode, (PropertyInfo)mbAssign.Member, newElement));
                            _sqlMapping.SetSqlEquivalent((PropertyInfo)mbAssign.Member, SqlAstNode);
                        }

                        newElement = false;
                    }
                    _request.ProjectionType = mbInit.NewExpression.Type;
                }
                else
                    throw new NotSupportedException("No supported select : " + select.ToString());

                _inOr = oldInOr;
            }
            else if (m.Method.Name == "GroupBy" && m.Method.GetParameters().Length == 2)
            {
                var groupBy = (LambdaExpression)StripQuotes(m.Arguments[1]);
                _request.FromAlias.Equivalents.Add(groupBy.Parameters[0]);

                if (groupBy.Body is MemberExpression && ((MemberExpression)groupBy.Body).Member is PropertyInfo)
                {
                    var memberExp = (MemberExpression)groupBy.Body;
                    if (!(memberExp.Expression is ParameterExpression) && !(memberExp.Expression is MemberExpression)) throw new NotSupportedException("Not supported group by : " + memberExp.ToString());

                    Visit(memberExp);
                    _request.GroupBy.Add(SqlAstNode);
                }
                else if (groupBy.Body is NewExpression)
                {
                    var newExp = (NewExpression)groupBy.Body;

                    for (var i = 0; i < newExp.Arguments.Count; i++)
                    {
                        var arg = newExp.Arguments[i] as MemberExpression;
                        if (arg == null || !(arg.Member is PropertyInfo)) throw new NotSupportedException("Only property access are supported : " + newExp.Arguments[i].ToString());

                        AddEquivalentProperty((PropertyInfo)arg.Member, (PropertyInfo)newExp.Members[i]);
                        Visit(arg);
                        _request.GroupBy.Add(SqlAstNode);
                    }
                }
                else
                    throw new NotSupportedException("No supported group by : " + groupBy.ToString());
            }
            else if (m.Method.Name.StartsWith("First", StringComparison.Ordinal) && m.Method.GetParameters().Length == 1)
            {
                _request.Take = 1;
            }
            else if (m.Method.Name == "Count")
            {
                if (_request.GroupBy.Count > 0)
                {
                    var countRequest = new Request();
                    countRequest.Select.Add(new SelectInfo(new DML.UnaryExpression(UnaryExpressionTypeEnum.Count, null), null, true));
                    countRequest.FromAlias = new AliasDefinition(_request);

                    if (_request.Select.Count == 0)
                        foreach (var grp in _request.GroupBy)
                            _request.Select.Add(new SelectInfo(grp, null, _request.Select.Count == 0));

                    _request = countRequest;
                }
                else
                {
                    _request.Select.Clear();
                    _request.Select.Add(new SelectInfo(new DML.UnaryExpression(UnaryExpressionTypeEnum.Count, null), null, true));
                }
            }
            else if ((m.Method.Name.StartsWith("OrderBy", StringComparison.Ordinal) || m.Method.Name.StartsWith("ThenBy", StringComparison.Ordinal)) && m.Method.GetParameters().Length == 2)
            {
                var orderBy = (LambdaExpression)StripQuotes(m.Arguments[1]);
                _request.FromAlias.Equivalents.Add(orderBy.Parameters[0]);

                Visit(orderBy.Body);

                if (m.Method.Name.StartsWith("OrderBy", StringComparison.Ordinal)) _request.Orders.Clear();
                _request.Orders.Add(new OrderElement(SqlAstNode, m.Method.Name == "OrderBy" || m.Method.Name == "ThenBy"));
            }
            else if (m.Method.Name == "Any")
            {
                var anyRequest = _request;
                if (anyRequest.Select.Count == 0) anyRequest.Select.Add(new DML.SelectInfo(new ColumnAccess(anyRequest.FromAlias, new DML.Identifier("*")), null, true));

                if (m.Method.GetParameters().Length == 2) VisitQueryableWhereOrFirst(m);

                _request = _request.ParentRequest;

                SqlAstNode = new DML.UnaryExpression(UnaryExpressionTypeEnum.Exists, anyRequest);
            }
            else if (m.Method.GetParameters().Length == 2)
            {
                var memberSelection = (LambdaExpression)StripQuotes(m.Arguments[1]);

                if (_request.Select.Count == 1 && _request.Select[0].SelectSql is AliasDefinition)
                    ((AliasDefinition)_request.Select[0].SelectSql).Equivalents.Add(memberSelection.Parameters[0]);
                else
                    _request.FromAlias.Equivalents.Add(memberSelection.Parameters[0]);

                Visit(memberSelection.Body);

                _request.Select.Clear();
                if (m.Method.Name == "Max")
                    _request.Select.Add(new SelectInfo(new DML.UnaryExpression(UnaryExpressionTypeEnum.Max, SqlAstNode), null, true));
                else if (m.Method.Name == "Min")
                    _request.Select.Add(new SelectInfo(new DML.UnaryExpression(UnaryExpressionTypeEnum.Min, SqlAstNode), null, true));
                else if (m.Method.Name == "Sum")
                    _request.Select.Add(new SelectInfo(new DML.UnaryExpression(UnaryExpressionTypeEnum.Sum, SqlAstNode), null, true));
                else if (m.Method.Name == "Average")
                    _request.Select.Add(new SelectInfo(new DML.UnaryExpression(UnaryExpressionTypeEnum.Average, SqlAstNode), null, true));
                else
                    throw new NotSupportedException($"The method '{m.Method.Name}' is not supported : {m.ToString()}");
            }
            else
                throw new NotSupportedException($"The method '{m.Method.Name}' is not supported : {m.ToString()}");

            return m;
        }

        private void VisitQueryableWhereOrFirst(MethodCallExpression m)
        {
            _request = SqlAstNode as Request ?? _request;

            var filter = (LambdaExpression)StripQuotes(m.Arguments[1]);
            _request.FromAlias.Equivalents.Add(filter.Parameters[0]);

            Visit(filter.Body);

            if (SqlAstNode is ColumnAccess)
            {
                // boolean member access
                SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Equal, SqlAstNode, new Constant(1, _sqlMapping));
            }

            if (m.Arguments[0].Type.GetGenericArguments()[0].IsGenericType && typeof(IGrouping<,>).IsAssignableFrom(m.Arguments[0].Type.GetGenericArguments()[0].GetGenericTypeDefinition()))
                _request.Having = SqlAstNode;
            else
            {
                _request.Where = _request.Where != null ? new DML.BinaryExpression(BinaryExpressionTypeEnum.And, _request.Where, SqlAstNode) : SqlAstNode;
                if (m.Method.Name.StartsWith("First", StringComparison.Ordinal)) _request.Take = 1;
            }
        }

        private void VisitNewExpressionArgument(NewExpression newExp, int i, Expression newExpArgument)
        {
            if (newExpArgument is ConstantExpression)
            {
                Visit(newExpArgument);
                _request.Select.Add(new SelectInfo(SqlAstNode, (PropertyInfo)newExp.Members[i], _request.Select.Count == 0));
                return;
            }

            if (newExpArgument is MemberExpression)
            {
                var arg = (MemberExpression)newExpArgument;
                if (!(arg.Member is PropertyInfo)) throw new NotSupportedException("Only property access are supported : " + arg.ToString());

                if (arg.Member.Name == "Key" && arg.Member.ReflectedType.IsGenericType && typeof(IGrouping<,>).IsAssignableFrom(arg.Member.ReflectedType.GetGenericTypeDefinition()))
                    foreach (var grp in _request.GroupBy) _request.Select.Add(new SelectInfo(grp, (PropertyInfo)newExp.Members[i], _request.Select.Count == 0));
                else
                {
                    AddEquivalentProperty((PropertyInfo)arg.Member, (PropertyInfo)newExp.Members[i]);

                    var alias = _request.FromAlias;
                    var expression = arg.Expression as MemberExpression;
                    while (expression != null)
                    {
                        alias = (AliasDefinition)_sqlMapping.GetSqlEquivalent(_request, alias, GetRealProperty((PropertyInfo)expression.Member), _inOr);
                        expression = expression.Expression as MemberExpression;
                    }

                    _request.Select.Add(new SelectInfo(_sqlMapping.GetSqlEquivalent(_request, alias, GetRealProperty((PropertyInfo)arg.Member), _inOr), (PropertyInfo)newExp.Members[i], _request.Select.Count == 0));
                }
            }
            else if (newExpArgument is MethodCallExpression)
            {
                var arg = (MethodCallExpression)newExpArgument;
                if (arg.Method.ReflectedType == typeof(Queryable))
                {
                    Visit(arg);
                    _request.ParentRequest.Select.Add(new SelectInfo(_request, (PropertyInfo)newExp.Members[i], _request.Select.Count == 0));
                    _request = _request.ParentRequest;
                }
                else
                {
                    if (!(arg.Method.ReflectedType == typeof(Enumerable)))
                        throw new NotSupportedException("Only method from Enumerable and queryable are supported : " + arg.ToString());
                    Visit(arg);
                    _request.Select.Add(new SelectInfo(SqlAstNode, (PropertyInfo)newExp.Members[i], _request.Select.Count == 0));
                }
            }
            else if (newExpArgument is NewExpression)
            {
                var arg = (NewExpression)newExpArgument;
                for (var j = 0; j < arg.Arguments.Count; j++) VisitNewExpressionArgument(arg, j, arg.Arguments[j]);
            }
            else if (newExpArgument is System.Linq.Expressions.BinaryExpression)
            {
                VisitBinary((System.Linq.Expressions.BinaryExpression)newExpArgument);
                _request.Select.Add(new SelectInfo(SqlAstNode, (PropertyInfo)newExp.Members[i], _request.Select.Count == 0));
                _sqlMapping.SetSqlEquivalent((PropertyInfo)newExp.Members[i], SqlAstNode);
            }
            else
                throw new NotSupportedException("Only property access and method from Enumerable are supported : " + newExp.Arguments[i].ToString());
        }

        private Expression VisitQueryableExtensionMethodCall(MethodCallExpression m)
        {
            Visit(m.Arguments[0]);

            if (m.Method.Name == "EagerLoad" || m.Method.Name == "LazyLoad" || m.Method.Name == "BatchLazyLoad")
                _mapper.LoadingPropertyInfos.Add(LoadingPropertyInfo.FromExpression(m));
            else if (m.Method.Name == "DontLoad")
            {
                var properties = new List<PropertyInfo>();

                var memberAccess = ((LambdaExpression)StripQuotes(m.Arguments[1])).Body as MemberExpression;
                if (memberAccess == null || !(memberAccess.Member is PropertyInfo)) throw new NotSupportedException("DontLoad is available for property only : " + m.ToString());

                properties.Add((PropertyInfo)memberAccess.Member);
                while (memberAccess.Expression is MemberExpression || memberAccess.Expression is MethodCallExpression)
                {
                    if (memberAccess.Expression is MethodCallExpression)
                    {
                        var call = (MethodCallExpression)memberAccess.Expression;
                        if (call.Method.Name != "First") throw new NotSupportedException("Only First method call is supported : " + m);
                        memberAccess = call.Arguments[0] as MemberExpression;
                        if (memberAccess == null) throw new NotSupportedException("Only First method call on collection property is supported : " + m);
                    }
                    else
                        memberAccess = (MemberExpression)memberAccess.Expression;
                    if (!(memberAccess.Member is PropertyInfo)) throw new NotSupportedException("DontLoad is available for property only : " + m.ToString());
                    properties.Insert(0, (PropertyInfo)memberAccess.Member);
                }

                _mapper.DontLoad(properties.ToArray());
            }
            else
                throw new NotSupportedException($"The method '{m.Method.Name}' is not supported : {m.ToString()}");

            return m;
        }

        private Expression VisitStringMethodCall(MethodCallExpression m)
        {
            Visit(m.Object);
            var str = SqlAstNode;
            Visit(m.Arguments[0]);
            var value = SqlAstNode;

            if (m.Method.Name == "StartsWith")
            {
                SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Like, str, new DML.BinaryExpression(BinaryExpressionTypeEnum.Concat, value, new Constant("%", _sqlMapping)));
                return m;
            }
            else if (m.Method.Name == "EndsWith")
            {
                SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Like, str, new DML.BinaryExpression(BinaryExpressionTypeEnum.Concat, new Constant("%", _sqlMapping), value));
                return m;
            }
            else if (m.Method.Name == "Contains")
            {
                SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Like, str,
                new DML.BinaryExpression(BinaryExpressionTypeEnum.Concat,
                    new Constant("%", _sqlMapping),
                    new DML.BinaryExpression(BinaryExpressionTypeEnum.Concat,
                        value,
                        new Constant("%", _sqlMapping))));
                return m;
            }
            else
                throw new NotSupportedException($"The method '{m.Method.Name}' is not supported : {m.ToString()}");
        }

        private void AddEquivalentProperty(PropertyInfo original, PropertyInfo equivalentProperty)
        {
            if (!original.Equals(equivalentProperty)) _equivalentProperties[equivalentProperty] = original;
        }

        private PropertyInfo GetRealProperty(PropertyInfo property)
        {
            if (!_equivalentProperties.ContainsKey(property)) return property;
            return GetRealProperty(_equivalentProperties[property]);
        }

        protected override Expression VisitUnary(System.Linq.Expressions.UnaryExpression u)
        {
            switch (u.NodeType)
            {
                case ExpressionType.Not:
                    Visit(u.Operand);
                    if (SqlAstNode is ColumnAccess) // boolean member access
                        SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Equal, SqlAstNode, new Constant(1, _sqlMapping));
                    SqlAstNode = new DML.UnaryExpression(UnaryExpressionTypeEnum.Not, SqlAstNode);
                    break;
                case ExpressionType.Convert:
                    Visit(u.Operand);
                    break;
                default:
                    if (u.Operand.Type.IsEnum)
                        Visit(u.Operand); // ignore convert to int
                    else
                        throw new NotSupportedException($"The unary operator '{u.NodeType}' is not supported");
                    break;
            }

            return u;
        }

        protected override Expression VisitBinary(System.Linq.Expressions.BinaryExpression b)
        {
            var oldInOr = _inOr;
            _inOr = _inOr || b.NodeType == ExpressionType.Or;

            Visit(b.Left);
            var left = SqlAstNode;

            Visit(b.Right);
            var right = SqlAstNode;

            if (b.NodeType == ExpressionType.And || b.NodeType == ExpressionType.AndAlso || b.NodeType == ExpressionType.Or || b.NodeType == ExpressionType.OrElse)
            {
                if (left is ColumnAccess) // boolean member access
                    left = new DML.BinaryExpression(BinaryExpressionTypeEnum.Equal, SqlAstNode, new Constant(1, _sqlMapping));

                if (right is ColumnAccess) // boolean member access
                    right = new DML.BinaryExpression(BinaryExpressionTypeEnum.Equal, SqlAstNode, new Constant(1, _sqlMapping));
            }

            switch (b.NodeType)
            {
                case ExpressionType.And:
                case ExpressionType.AndAlso:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.And, left, right);
                    break;
                case ExpressionType.Or:
                case ExpressionType.OrElse:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Or, left, right);
                    break;
                case ExpressionType.Equal:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Equal, left, right);
                    break;
                case ExpressionType.NotEqual:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.NotEqual, left, right);
                    break;
                case ExpressionType.LessThan:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.LessThan, left, right);
                    break;
                case ExpressionType.LessThanOrEqual:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.LessThanOrEqual, left, right);
                    break;
                case ExpressionType.GreaterThan:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.GreaterThan, left, right);
                    break;
                case ExpressionType.GreaterThanOrEqual:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.GreaterThanOrEqual, left, right);
                    break;
                case ExpressionType.Coalesce:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Coalesce, left, right);
                    break;
                case ExpressionType.Add:
                    if (b.Left.Type == typeof(string))
                        SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Concat, left, right);
                    else
                        SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Add, left, right);
                    break;
                case ExpressionType.Subtract:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Subtract, left, right);
                    break;
                case ExpressionType.Multiply:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Multiply, left, right);
                    break;
                case ExpressionType.Divide:
                    SqlAstNode = new DML.BinaryExpression(BinaryExpressionTypeEnum.Divide, left, right);
                    break;
                default:
                    throw new NotSupportedException($"The binary operator '{b.NodeType}' is not supported");
            }

            _inOr = oldInOr;

            return b;
        }

        protected override Expression VisitConditional(System.Linq.Expressions.ConditionalExpression node)
        {
            Visit(node.Test);
            var condition = SqlAstNode;
            Visit(node.IfTrue);
            var thenExpression = SqlAstNode;
            Visit(node.IfFalse);
            var elseExpression = SqlAstNode;

            SqlAstNode = new DML.ConditionalExpression(condition, thenExpression, elseExpression);

            return node;
        }

        protected override Expression VisitConstant(ConstantExpression c)
        {
            var q = c.Value as IQueryable;
            if (q != null)
            {
                _request = new Request(_request);
                _request.ProjectionType = q.ElementType;
                _request.FromAlias = new AliasDefinition(_sqlMapping.GetSqlEquivalent(q.ElementType));
                SqlAstNode = _request;
            }
            else if (c.Value == null)
                SqlAstNode = new Constant(null, _sqlMapping);
            else if (Type.GetTypeCode(c.Value.GetType()) == TypeCode.Object && !c.Value.GetType().IsArray)
            {
                var idProperty = _sqlMapping.GetIdProperty(c.Value.GetType());
                if (idProperty == null) throw new NotSupportedException($"The constant for '{c.Value}' is not supported");
                SqlAstNode = new Constant(idProperty.GetValue(c.Value), _sqlMapping);
            }
            else
                SqlAstNode = new Constant(c.Value, _sqlMapping);

            return c;
        }

        protected override Expression VisitMember(MemberExpression m)
        {
            Visit(m.Expression);

            if (m.Member.Name == "Key" && m.Member.ReflectedType.IsGenericType && typeof(IGrouping<,>).IsAssignableFrom(m.Member.ReflectedType.GetGenericTypeDefinition()))
                SqlAstNode = _request.GroupBy[0];
            else
            {
                var property = (PropertyInfo)m.Member;
                if (property.ReflectedType != m.Expression.Type) property = m.Expression.Type.GetProperty(property.Name);
                SqlAstNode = _sqlMapping.GetSqlEquivalent(_request, (AliasDefinition)SqlAstNode, GetRealProperty(property), _inOr);
            }
            return m;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (typeof(IQueryable).IsAssignableFrom(node.Type))
            {
                _request = new Request(_request);
                _request.FromAlias = new AliasDefinition(_sqlMapping.GetSqlEquivalent(node.Type.GetGenericArguments()[0]));
                _request.IdColumnName = _sqlMapping.GetColumnName(_sqlMapping.GetIdProperty(node.Type.GetGenericArguments()[0]));

                SqlAstNode = _request;
                return node;
            }

            SqlAstNode = _request?.GetAliasFor(node);
            return node;
        }
    }
}
