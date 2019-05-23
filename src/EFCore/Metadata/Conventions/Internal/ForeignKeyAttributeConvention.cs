// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
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
    public class ForeignKeyAttributeConvention : IForeignKeyAddedConvention, IModelFinalizedConvention
    {
        private readonly IMemberClassifier _memberClassifier;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public ForeignKeyAttributeConvention(
            [NotNull] IMemberClassifier memberClassifier,
            [NotNull] IDiagnosticsLogger<DbLoggerCategory.Model> logger)
        {
            Check.NotNull(memberClassifier, nameof(memberClassifier));

            _memberClassifier = memberClassifier;
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
            Check.NotNull(relationshipBuilder, nameof(relationshipBuilder));

            var foreignKey = relationshipBuilder.Metadata;

            var fkPropertyOnPrincipal
                = FindForeignKeyAttributeOnProperty(foreignKey.PrincipalEntityType, foreignKey.PrincipalToDependent?.Name);

            var fkPropertyOnDependent
                = FindForeignKeyAttributeOnProperty(foreignKey.DeclaringEntityType, foreignKey.DependentToPrincipal?.Name);

            if (fkPropertyOnDependent != null
                && fkPropertyOnPrincipal != null)
            {
                Logger.ForeignKeyAttributesOnBothPropertiesWarning(
                    foreignKey.PrincipalToDependent,
                    foreignKey.DependentToPrincipal,
                    fkPropertyOnPrincipal,
                    fkPropertyOnDependent);

                relationshipBuilder = SplitNavigationsToSeparateRelationships(relationshipBuilder);
                if (relationshipBuilder == null)
                {
                    context.StopProcessing();
                    return;
                }

                fkPropertyOnPrincipal = null;
            }

            var fkPropertiesOnPrincipalToDependent
                = FindCandidateDependentPropertiesThroughNavigation(relationshipBuilder, pointsToPrincipal: false);

            var fkPropertiesOnDependentToPrincipal
                = FindCandidateDependentPropertiesThroughNavigation(relationshipBuilder, pointsToPrincipal: true);

            if (fkPropertiesOnDependentToPrincipal != null
                && fkPropertiesOnPrincipalToDependent != null)
            {
                Logger.ForeignKeyAttributesOnBothNavigationsWarning(
                    relationshipBuilder.Metadata.DependentToPrincipal, relationshipBuilder.Metadata.PrincipalToDependent);

                relationshipBuilder = SplitNavigationsToSeparateRelationships(relationshipBuilder);
                if (relationshipBuilder == null)
                {
                    context.StopProcessing();
                    return;
                }

                fkPropertiesOnPrincipalToDependent = null;
            }

            var fkPropertiesOnNavigation = fkPropertiesOnDependentToPrincipal ?? fkPropertiesOnPrincipalToDependent;
            var upgradePrincipalToDependentNavigationSource = fkPropertiesOnPrincipalToDependent != null;
            var upgradeDependentToPrincipalNavigationSource = fkPropertiesOnDependentToPrincipal != null;
            var shouldInvert = false;
            IReadOnlyList<string> fkPropertiesToSet;

            if (fkPropertiesOnNavigation == null
                || fkPropertiesOnNavigation.Count == 0)
            {
                if (fkPropertyOnDependent == null
                    && fkPropertyOnPrincipal == null)
                {
                    return;
                }

                if (fkPropertyOnDependent != null)
                {
                    fkPropertiesToSet = new List<string>
                    {
                        fkPropertyOnDependent.GetSimpleMemberName()
                    };
                    upgradeDependentToPrincipalNavigationSource = true;
                }
                else
                {
                    if (foreignKey.PrincipalToDependent.IsCollection())
                    {
                        context.StopProcessing();
                        return;
                    }

                    shouldInvert = true;
                    fkPropertiesToSet = new List<string>
                    {
                        fkPropertyOnPrincipal.GetSimpleMemberName()
                    };
                    upgradePrincipalToDependentNavigationSource = true;
                }
            }
            else
            {
                fkPropertiesToSet = fkPropertiesOnNavigation;

                if (fkPropertyOnDependent == null
                    && fkPropertyOnPrincipal == null)
                {
                    if (fkPropertiesOnPrincipalToDependent != null
                        && foreignKey.IsUnique)
                    {
                        shouldInvert = true;
                    }
                }
                else
                {
                    var fkProperty = fkPropertyOnDependent ?? fkPropertyOnPrincipal;
                    if (fkPropertiesOnNavigation.Count != 1
                        || !Equals(fkPropertiesOnNavigation.First(), fkProperty.GetSimpleMemberName()))
                    {
                        Logger.ConflictingForeignKeyAttributesOnNavigationAndPropertyWarning(
                            fkPropertiesOnDependentToPrincipal != null
                                ? relationshipBuilder.Metadata.DependentToPrincipal
                                : relationshipBuilder.Metadata.PrincipalToDependent,
                            fkProperty);

                        relationshipBuilder = SplitNavigationsToSeparateRelationships(relationshipBuilder);
                        if (relationshipBuilder == null)
                        {
                            context.StopProcessing();
                            return;
                        }

                        fkPropertiesToSet = fkPropertiesOnDependentToPrincipal ?? new List<string>
                        {
                            fkPropertyOnDependent.GetSimpleMemberName()
                        };
                    }

                    if (fkPropertyOnDependent != null)
                    {
                        upgradeDependentToPrincipalNavigationSource = true;
                    }
                    else
                    {
                        shouldInvert = true;
                    }
                }
            }

            var newRelationshipBuilder = relationshipBuilder;

            if (upgradeDependentToPrincipalNavigationSource)
            {
                newRelationshipBuilder.Metadata.UpdateDependentToPrincipalConfigurationSource(ConfigurationSource.DataAnnotation);
            }

            if (upgradePrincipalToDependentNavigationSource)
            {
                newRelationshipBuilder.Metadata.UpdatePrincipalToDependentConfigurationSource(ConfigurationSource.DataAnnotation);
            }

            if (shouldInvert)
            {
                newRelationshipBuilder = newRelationshipBuilder.HasEntityTypes(
                    foreignKey.DeclaringEntityType, foreignKey.PrincipalEntityType, fromDataAnnotation: true);
            }
            else
            {
                var existingProperties = foreignKey.DeclaringEntityType.FindProperties(fkPropertiesToSet);
                if (existingProperties != null)
                {
                    var conflictingFk = foreignKey.DeclaringEntityType.FindForeignKeys(existingProperties)
                        .FirstOrDefault(
                            fk => fk != foreignKey
                                  && fk.PrincipalEntityType == foreignKey.PrincipalEntityType
                                  && fk.GetConfigurationSource() == ConfigurationSource.DataAnnotation
                                  && fk.GetPropertiesConfigurationSource() == ConfigurationSource.DataAnnotation);
                    if (conflictingFk != null)
                    {
                        throw new InvalidOperationException(
                            CoreStrings.ConflictingForeignKeyAttributes(
                                existingProperties.Format(),
                                foreignKey.DeclaringEntityType.DisplayName()));
                    }
                }
            }

            newRelationshipBuilder = newRelationshipBuilder?.HasForeignKey(
                foreignKey.DeclaringEntityType.Builder.GetOrCreateProperties(fkPropertiesToSet, fromDataAnnotation: true),
                fromDataAnnotation: true);

            if (newRelationshipBuilder != null
                && newRelationshipBuilder != relationshipBuilder)
            {
                context.StopProcessing(newRelationshipBuilder);
            }
        }

        private static IConventionRelationshipBuilder SplitNavigationsToSeparateRelationships(IConventionRelationshipBuilder relationshipBuilder)
        {
            var foreignKey = relationshipBuilder.Metadata;
            var dependentToPrincipalNavigationName = foreignKey.DependentToPrincipal?.Name;
            var principalToDependentNavigationName = foreignKey.PrincipalToDependent?.Name;

            if (GetInversePropertyAttribute(foreignKey.PrincipalToDependent) != null
                || GetInversePropertyAttribute(foreignKey.DependentToPrincipal) != null)
            {
                // Relationship is joined by InversePropertyAttribute
                throw new InvalidOperationException(
                    CoreStrings.InvalidRelationshipUsingDataAnnotations(
                        dependentToPrincipalNavigationName,
                        foreignKey.DeclaringEntityType.DisplayName(),
                        principalToDependentNavigationName,
                        foreignKey.PrincipalEntityType.DisplayName()));
            }

            relationshipBuilder = relationshipBuilder.HasNavigation(
                (string)null,
                pointsToPrincipal: false,
                fromDataAnnotation: true);
            return relationshipBuilder == null
                ? null
                : foreignKey.PrincipalEntityType.Builder.HasRelationship(
                      foreignKey.DeclaringEntityType,
                      principalToDependentNavigationName,
                      null,
                      fromDataAnnotation: true) == null
                    ? null
                    : relationshipBuilder;
        }

        private static ForeignKeyAttribute GetForeignKeyAttribute(IConventionTypeBase entityType, string propertyName)
            => entityType.GetRuntimeProperties()?.Values
                .FirstOrDefault(
                    p => string.Equals(p.GetSimpleMemberName(), propertyName, StringComparison.OrdinalIgnoreCase)
                         && Attribute.IsDefined(p, typeof(ForeignKeyAttribute), inherit: true))
                ?.GetCustomAttribute<ForeignKeyAttribute>(inherit: true);

        private static ForeignKeyAttribute GetForeignKeyAttribute(IConventionNavigation navigation)
            => GetAttribute<ForeignKeyAttribute>(navigation.PropertyInfo);

        private static InversePropertyAttribute GetInversePropertyAttribute(IConventionNavigation navigation)
            => GetAttribute<InversePropertyAttribute>(navigation.PropertyInfo);

        private static TAttribute GetAttribute<TAttribute>(MemberInfo memberInfo)
            where TAttribute : Attribute
        {
            if (memberInfo == null
                || !Attribute.IsDefined(memberInfo, typeof(TAttribute), inherit: true))
            {
                return null;
            }

            return memberInfo.GetCustomAttribute<TAttribute>(inherit: true);
        }

        [ContractAnnotation("navigationName:null => null")]
        private MemberInfo FindForeignKeyAttributeOnProperty(IConventionEntityType entityType, string navigationName)
        {
            if (string.IsNullOrWhiteSpace(navigationName)
                || !entityType.HasClrType())
            {
                return null;
            }

            MemberInfo candidateProperty = null;

            foreach (var memberInfo in entityType.GetRuntimeProperties().Values.Cast<MemberInfo>()
                .Concat(entityType.GetRuntimeFields().Values))
            {
                if (entityType.Builder.IsIgnored(memberInfo.GetSimpleMemberName())
                    || !Attribute.IsDefined(memberInfo, typeof(ForeignKeyAttribute), inherit: true))
                {
                    continue;
                }

                var attribute = memberInfo.GetCustomAttribute<ForeignKeyAttribute>(inherit: true);

                if (attribute.Name != navigationName
                    || (memberInfo is PropertyInfo propertyInfo
                        && FindCandidateNavigationPropertyType(propertyInfo) != null))
                {
                    continue;
                }

                if (candidateProperty != null)
                {
                    throw new InvalidOperationException(
                        CoreStrings.CompositeFkOnProperty(navigationName, entityType.DisplayName()));
                }

                candidateProperty = memberInfo;
            }

            if (candidateProperty != null)
            {
                var fkAttributeOnNavigation = GetForeignKeyAttribute(entityType, navigationName);
                if (fkAttributeOnNavigation != null
                    && fkAttributeOnNavigation.Name != candidateProperty.GetSimpleMemberName())
                {
                    throw new InvalidOperationException(
                        CoreStrings.FkAttributeOnPropertyNavigationMismatch(
                            candidateProperty.Name, navigationName, entityType.DisplayName()));
                }
            }

            return candidateProperty;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected virtual Type FindCandidateNavigationPropertyType([NotNull] PropertyInfo propertyInfo)
            => _memberClassifier.FindCandidateNavigationPropertyType(propertyInfo);

        private static IReadOnlyList<string> FindCandidateDependentPropertiesThroughNavigation(
            IConventionRelationshipBuilder relationshipBuilder,
            bool pointsToPrincipal)
        {
            var navigation = pointsToPrincipal
                ? relationshipBuilder.Metadata.DependentToPrincipal
                : relationshipBuilder.Metadata.PrincipalToDependent;

            var navigationFkAttribute = navigation != null
                ? GetForeignKeyAttribute(navigation)
                : null;

            if (navigationFkAttribute != null)
            {
                var properties = navigationFkAttribute.Name.Split(',').Select(p => p.Trim()).ToList();

                if (properties.Any(string.IsNullOrWhiteSpace))
                {
                    throw new InvalidOperationException(
                        CoreStrings.InvalidPropertyListOnNavigation(navigation.Name, navigation.DeclaringEntityType.DisplayName()));
                }

                var navigationPropertyTargetType =
                    navigation.DeclaringEntityType.GetRuntimeProperties()[navigation.Name].PropertyType;

                var otherNavigations = navigation.DeclaringEntityType.GetRuntimeProperties().Values
                    .Where(p => p.PropertyType == navigationPropertyTargetType && p.GetSimpleMemberName() != navigation.Name)
                    .OrderBy(p => p.GetSimpleMemberName());

                foreach (var propertyInfo in otherNavigations)
                {
                    var attribute = GetAttribute<ForeignKeyAttribute>(propertyInfo);
                    if (attribute?.Name == navigationFkAttribute.Name)
                    {
                        throw new InvalidOperationException(
                            CoreStrings.MultipleNavigationsSameFk(navigation.DeclaringEntityType.DisplayName(), attribute.Name));
                    }
                }

                return properties;
            }

            return null;
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
                foreach (var declaredNavigation in entityType.GetDeclaredNavigations())
                {
                    if (declaredNavigation.IsCollection())
                    {
                        var foreignKey = declaredNavigation.ForeignKey;
                        var fkPropertyOnPrincipal
                            = FindForeignKeyAttributeOnProperty(foreignKey.PrincipalEntityType, declaredNavigation.Name);
                        if (fkPropertyOnPrincipal != null)
                        {
                            throw new InvalidOperationException(
                                CoreStrings.FkAttributeOnNonUniquePrincipal(
                                    declaredNavigation.Name,
                                    foreignKey.PrincipalEntityType.DisplayName(),
                                    foreignKey.DeclaringEntityType.DisplayName()));
                        }
                    }
                }
            }
        }
    }
}
