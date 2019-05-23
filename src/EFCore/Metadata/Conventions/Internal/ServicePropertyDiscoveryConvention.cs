// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class ServicePropertyDiscoveryConvention :
        IEntityTypeAddedConvention,
        IEntityTypeBaseTypeChangedConvention,
        IEntityTypeMemberIgnoredConvention,
        IModelFinalizedConvention
    {
        private readonly ITypeMappingSource _typeMappingSource;
        private readonly IParameterBindingFactories _parameterBindingFactories;

        private const string DuplicateServicePropertiesAnnotationName = "RelationshipDiscoveryConvention:DuplicateServiceProperties";

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public ServicePropertyDiscoveryConvention(
            [NotNull] ITypeMappingSource typeMappingSource,
            [NotNull] IParameterBindingFactories parameterBindingFactories,
            [NotNull] IDiagnosticsLogger<DbLoggerCategory.Model> logger)
        {
            Check.NotNull(typeMappingSource, nameof(typeMappingSource));
            Check.NotNull(parameterBindingFactories, nameof(parameterBindingFactories));

            _typeMappingSource = typeMappingSource;
            _parameterBindingFactories = parameterBindingFactories;
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
        ///     Called after an entity type is added to the model.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessEntityTypeAdded(
            IConventionEntityTypeBuilder entityTypeBuilder, IConventionContext<IConventionEntityTypeBuilder> context)
        {
            Process(entityTypeBuilder);
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
            => Process(entityTypeBuilder);

        private void Process(IConventionEntityTypeBuilder entityTypeBuilder)
        {
            var entityType = entityTypeBuilder.Metadata;

            if (!entityType.HasClrType())
            {
                return;
            }

            var candidates = entityType.GetRuntimeProperties().Values;

            foreach (var propertyInfo in candidates)
            {
                if (entityTypeBuilder.IsIgnored(propertyInfo.GetSimpleMemberName())
                    || ConventionEntityTypeExtensions.FindProperty(entityType, propertyInfo) != null
                    || entityType.FindNavigation(propertyInfo) != null
                    || !propertyInfo.IsCandidateProperty(publicOnly: false)
                    || (propertyInfo.IsCandidateProperty()
                        && _typeMappingSource.FindMapping(propertyInfo) != null))
                {
                    continue;
                }

                var factory = _parameterBindingFactories.FindFactory(propertyInfo.PropertyType, propertyInfo.GetSimpleMemberName());
                if (factory == null)
                {
                    continue;
                }

                var duplicateMap = GetDuplicateServiceProperties(entityType);
                if (duplicateMap != null
                    && duplicateMap.TryGetValue(propertyInfo.PropertyType, out var duplicateServiceProperties))
                {
                    duplicateServiceProperties.Add(propertyInfo);

                    return;
                }

                var otherServicePropertySameType = entityType.GetServiceProperties()
                    .FirstOrDefault(p => p.ClrType == propertyInfo.PropertyType);
                if (otherServicePropertySameType != null)
                {
                    if (ConfigurationSource.Convention.Overrides(otherServicePropertySameType.GetConfigurationSource()))
                    {
                        otherServicePropertySameType.DeclaringEntityType.RemoveServiceProperty(otherServicePropertySameType.Name);
                    }

                    AddDuplicateServiceProperty(entityTypeBuilder, propertyInfo);
                    AddDuplicateServiceProperty(entityTypeBuilder, otherServicePropertySameType.GetIdentifyingMemberInfo());

                    return;
                }

                entityTypeBuilder.ServiceProperty(propertyInfo)?.HasParameterBinding(
                    (ServiceParameterBinding)factory.Bind(entityType, propertyInfo.PropertyType, propertyInfo.GetSimpleMemberName()));
            }
        }

        /// <summary>
        ///     Called after an entity type member is ignored.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type. </param>
        /// <param name="name"> The name of the ignored member. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessEntityTypeMemberIgnored(
            IConventionEntityTypeBuilder entityTypeBuilder, string name, IConventionContext<string> context)
        {
            var entityType = entityTypeBuilder.Metadata;
            var duplicateMap = GetDuplicateServiceProperties(entityType);
            if (duplicateMap == null)
            {
                return;
            }

            var member = (MemberInfo)entityType.GetRuntimeProperties().Find(name)
                         ?? entityType.GetRuntimeFields().Find(name);
            var type = member.GetMemberType();
            if (duplicateMap.TryGetValue(type, out var duplicateServiceProperties)
                && duplicateServiceProperties.Remove(member))
            {
                if (duplicateServiceProperties.Count != 1)
                {
                    return;
                }

                var otherMember = duplicateServiceProperties.First();
                var otherName = otherMember.GetSimpleMemberName();
                var factory = _parameterBindingFactories.FindFactory(type, otherName);
                entityType.Builder.ServiceProperty(otherMember)?.HasParameterBinding(
                    (ServiceParameterBinding)factory.Bind(entityType, type, otherName));
                duplicateMap.Remove(type);
                if (duplicateMap.Count == 0)
                {
                    SetDuplicateServiceProperties(entityType.Builder, null);
                }
            }
        }

        /// <summary>
        ///     Called after a model is finalized.
        /// </summary>
        /// <param name="modelBuilder"> The builder for the model. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessModelFinalized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                var duplicateMap = GetDuplicateServiceProperties(entityType);
                if (duplicateMap == null)
                {
                    continue;
                }

                foreach (var duplicateServiceProperties in duplicateMap.Values)
                {
                    foreach (var duplicateServiceProperty in duplicateServiceProperties)
                    {
                        if (entityType.FindProperty(duplicateServiceProperty.GetSimpleMemberName()) == null
                            && entityType.FindNavigation(duplicateServiceProperty.GetSimpleMemberName()) == null)
                        {
                            throw new InvalidOperationException(
                                CoreStrings.AmbiguousServiceProperty(
                                    duplicateServiceProperty.Name,
                                    duplicateServiceProperty.GetMemberType().ShortDisplayName(),
                                    entityType.DisplayName()));
                        }
                    }
                }

                SetDuplicateServiceProperties(entityType.Builder, null);
            }
        }

        private static void AddDuplicateServiceProperty(IConventionEntityTypeBuilder entityTypeBuilder, MemberInfo serviceProperty)
        {
            var duplicateMap = GetDuplicateServiceProperties(entityTypeBuilder.Metadata)
                               ?? new Dictionary<Type, HashSet<MemberInfo>>(1);

            var type = serviceProperty.GetMemberType();
            if (!duplicateMap.TryGetValue(type, out var duplicateServiceProperties))
            {
                duplicateServiceProperties = new HashSet<MemberInfo>();
                duplicateMap[type] = duplicateServiceProperties;
            }

            duplicateServiceProperties.Add(serviceProperty);

            SetDuplicateServiceProperties(entityTypeBuilder, duplicateMap);
        }

        private static Dictionary<Type, HashSet<MemberInfo>> GetDuplicateServiceProperties(IConventionEntityType entityType)
            => entityType.FindAnnotation(DuplicateServicePropertiesAnnotationName)?.Value
                as Dictionary<Type, HashSet<MemberInfo>>;

        private static void SetDuplicateServiceProperties(
            IConventionEntityTypeBuilder entityTypeBuilder,
            Dictionary<Type, HashSet<MemberInfo>> duplicateServiceProperties)
            => entityTypeBuilder.HasAnnotation(DuplicateServicePropertiesAnnotationName, duplicateServiceProperties);
    }
}
