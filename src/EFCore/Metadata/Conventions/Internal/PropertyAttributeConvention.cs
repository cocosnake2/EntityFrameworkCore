// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public abstract class PropertyAttributeConvention<TAttribute> : IPropertyAddedConvention, IPropertyFieldChangedConvention
        where TAttribute : Attribute
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected PropertyAttributeConvention([NotNull] IDiagnosticsLogger<DbLoggerCategory.Model> logger)
        {
            Logger = logger;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected virtual IDiagnosticsLogger<DbLoggerCategory.Model> Logger { get; }

        /// <summary>
        ///     Called after a property is added to the entity type.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessPropertyAdded(
            IConventionPropertyBuilder propertyBuilder,
            IConventionContext<IConventionPropertyBuilder> context)
        {
            Check.NotNull(propertyBuilder, nameof(propertyBuilder));

            var memberInfo = propertyBuilder.Metadata.GetIdentifyingMemberInfo();
            if (memberInfo == null)
            {
                return;
            }

            Process(propertyBuilder, memberInfo, (IReadableConventionContext)context);
        }

        /// <summary>
        ///     Called after the backing field for a property is changed.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property. </param>
        /// <param name="newFieldInfo"> The new field. </param>
        /// <param name="oldFieldInfo"> The old field. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessPropertyFieldChanged(
            IConventionPropertyBuilder propertyBuilder,
            FieldInfo newFieldInfo,
            FieldInfo oldFieldInfo,
            IConventionContext<FieldInfo> context)
        {
            if (newFieldInfo != null
                && propertyBuilder.Metadata.PropertyInfo == null)
            {
                Process(propertyBuilder, newFieldInfo, (IReadableConventionContext)context);
            }
        }

        private void Process(IConventionPropertyBuilder propertyBuilder, MemberInfo memberInfo, IReadableConventionContext context)
        {
            if (!Attribute.IsDefined(memberInfo, typeof(TAttribute), inherit: true))
            {
                return;
            }

            var attributes = memberInfo.GetCustomAttributes<TAttribute>(inherit: true);

            foreach (var attribute in attributes)
            {
                ProcessPropertyAdded(propertyBuilder, attribute, memberInfo, context);
                if (context.ShouldStopProcessing())
                {
                    break;
                }
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected abstract void ProcessPropertyAdded(
            [NotNull] IConventionPropertyBuilder propertyBuilder,
            [NotNull] TAttribute attribute,
            [NotNull] MemberInfo clrMember,
            [NotNull] IConventionContext context);
    }
}
