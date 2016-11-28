﻿using F23Bag.AutomaticUI.Layouts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace F23Bag.Tests.AutomaticUITestElements
{
    public class ObjectForTabsLayoutProvider : ILayoutProvider
    {
        public Type LayoutFor
        {
            get
            {
                return typeof(ObjectForTabsLayout);
            }
        }

        public IEnumerable<Layout> GetLayouts(Type dataType, IEnumerable<ILayoutProvider> layoutProviders)
        {
            return new LayoutBuilder<ObjectForTabsLayout>(dataType, layoutProviders)
                .Tabs(t => t
                    .Tab("FirstTab", l => l
                        .Vertical(v => v
                            .Property(o => o.P1)
                            .Property(o => o.P3)))
                    .Tab("SecondTab", l => l
                        .Horizontal(h => h
                            .Property(o => o.P2)
                            .Property(o => o.P4))))
                .GetLayouts();
        }
    }
}
