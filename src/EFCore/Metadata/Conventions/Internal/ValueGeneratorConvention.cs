// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class ValueGeneratorConvention :
        IEntityTypePrimaryKeyChangedConvention,
        IForeignKeyAddedConvention,
        IForeignKeyRemovedConvention,
        IForeignKeyPropertiesChangedConvention,
        IEntityTypeBaseTypeChangedConvention
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public ValueGeneratorConvention([NotNull] IDiagnosticsLogger<DbLoggerCategory.Model> logger)
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
        ///     Called after a foreign key is added to the entity type.
        /// </summary>
        /// <param name="relationshipBuilder"> The builder for the foreign key. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessForeignKeyAdded(
            IConventionRelationshipBuilder relationshipBuilder, IConventionContext<IConventionRelationshipBuilder> context)
        {
            foreach (var property in relationshipBuilder.Metadata.Properties)
            {
                property.Builder.ValueGenerated(ValueGenerated.Never);
            }
        }

        /// <summary>
        ///     Called after a foreign key is removed.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type. </param>
        /// <param name="foreignKey"> The removed foreign key. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessForeignKeyRemoved(
            IConventionEntityTypeBuilder entityTypeBuilder, IConventionForeignKey foreignKey,
            IConventionContext<IConventionForeignKey> context)
        {
            OnForeignKeyRemoved(foreignKey.Properties);
        }

        /// <summary>
        ///     Called after the foreign key properties or principal key are changed.
        /// </summary>
        /// <param name="relationshipBuilder"> The builder for the foreign key. </param>
        /// <param name="oldDependentProperties"> The old foreign key properties. </param>
        /// <param name="oldPrincipalKey"> The old principal key. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessForeignKeyPropertiesChanged(
            IConventionRelationshipBuilder relationshipBuilder,
            IReadOnlyList<IConventionProperty> oldDependentProperties,
            IConventionKey oldPrincipalKey,
            IConventionContext<IConventionRelationshipBuilder> context)
        {
            var foreignKey = relationshipBuilder.Metadata;
            if (!foreignKey.Properties.SequenceEqual(oldDependentProperties))
            {
                OnForeignKeyRemoved(oldDependentProperties);

                if (relationshipBuilder.Metadata.Builder != null)
                {
                    foreach (var property in foreignKey.Properties)
                    {
                        property.Builder.ValueGenerated(ValueGenerated.Never);
                    }
                }
            }
        }

        private void OnForeignKeyRemoved(IReadOnlyList<IConventionProperty> foreignKeyProperties)
        {
            foreach (var property in foreignKeyProperties)
            {
                var pk = property.FindContainingPrimaryKey();
                if (pk == null)
                {
                    property.Builder?.ValueGenerated(GetValueGenerated(property));
                }
                else
                {
                    foreach (var keyProperty in pk.Properties)
                    {
                        keyProperty.Builder.ValueGenerated(GetValueGenerated(property));
                    }
                }
            }
        }

        /// <summary>
        ///     Called after the primary key for an entity type is changed.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type. </param>
        /// <param name="newPrimaryKey"> The new primary key. </param>
        /// <param name="previousPrimaryKey"> The old primary key. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessEntityTypePrimaryKeyChanged(
            IConventionEntityTypeBuilder entityTypeBuilder,
            IConventionKey newPrimaryKey,
            IConventionKey previousPrimaryKey,
            IConventionContext<IConventionKey> context)
        {
            if (previousPrimaryKey != null)
            {
                foreach (var property in previousPrimaryKey.Properties)
                {
                    property.Builder?.ValueGenerated(ValueGenerated.Never);
                }
            }

            if (newPrimaryKey != null)
            {
                foreach (var property in newPrimaryKey.Properties)
                {
                    property.Builder.ValueGenerated(GetValueGenerated(property));
                }
            }
        }

        /// <summary>
        ///     Called after the base type of an entity type changes.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type. </param>
        /// <param name="newBaseType"> The new base entity type. </param>
        /// <param name="oldBaseType"> The old base entity type. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessEntityTypeBaseTypeChanged(
            IConventionEntityTypeBuilder entityTypeBuilder,
            IConventionEntityType newBaseType,
            IConventionEntityType oldBaseType,
            IConventionContext<IConventionEntityType> context)
        {
            foreach (var property in entityTypeBuilder.Metadata.GetProperties())
            {
                property.Builder.ValueGenerated(GetValueGenerated(property));
            }
        }

        /// <summary>
        ///     Returns the store value generation strategy to set for the given property.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> The store value generation strategy to set for the given property. </returns>
        public virtual ValueGenerated? GetValueGenerated([NotNull] IConventionProperty property)
            => !property.IsForeignKey()
               && property.FindContainingPrimaryKey()?.Properties.Count(p => !p.IsForeignKey()) == 1
               && CanBeGenerated(property)
                ? ValueGenerated.OnAdd
                : (ValueGenerated?)null;

        /// <summary>
        ///     Indicates whether the specified property can have the value generated by the store or by a non-temporary value generator
        ///     when not set.
        /// </summary>
        /// <param name="property"> The key property that might be store generated. </param>
        /// <returns> A value indicating whether the specified property should have the value generated by the store. </returns>
        private static bool CanBeGenerated(IConventionProperty property)
        {
            var propertyType = property.ClrType.UnwrapNullableType();
            return (propertyType.IsInteger()
                    && propertyType != typeof(byte))
                   || propertyType == typeof(Guid);
        }
    }
}
