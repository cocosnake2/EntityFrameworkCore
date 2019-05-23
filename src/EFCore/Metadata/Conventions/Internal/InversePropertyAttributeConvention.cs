// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    public class InversePropertyAttributeConvention :
        NavigationAttributeEntityTypeConvention<InversePropertyAttribute>, IModelFinalizedConvention
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public InversePropertyAttributeConvention(
            [NotNull] IMemberClassifier memberClassifier,
            [NotNull] IDiagnosticsLogger<DbLoggerCategory.Model> logger)
            : base(memberClassifier, logger)
        {
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public const string InverseNavigationsAnnotationName = "InversePropertyAttributeConvention:InverseNavigations";

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override void ProcessEntityTypeAdded(
            IConventionEntityTypeBuilder entityTypeBuilder,
            MemberInfo navigationMemberInfo,
            Type targetClrType,
            InversePropertyAttribute attribute,
            IConventionContext<IConventionEntityTypeBuilder> context)
        {
            Check.NotNull(entityTypeBuilder, nameof(entityTypeBuilder));
            Check.NotNull(navigationMemberInfo, nameof(navigationMemberInfo));
            Check.NotNull(attribute, nameof(attribute));

            if (!entityTypeBuilder.CanAddNavigation(
                navigationMemberInfo.GetSimpleMemberName(), fromDataAnnotation: true))
            {
                return;
            }

            var targetEntityTypeBuilder = RelationshipDiscoveryConvention.GetTargetEntityTypeBuilder(
                entityTypeBuilder, targetClrType, navigationMemberInfo, ConfigurationSource.DataAnnotation);

            if (targetEntityTypeBuilder == null)
            {
                return;
            }

            ConfigureInverseNavigation(entityTypeBuilder, navigationMemberInfo, targetEntityTypeBuilder, attribute);
        }

        private IConventionRelationshipBuilder ConfigureInverseNavigation(
            IConventionEntityTypeBuilder entityTypeBuilder,
            MemberInfo navigationMemberInfo,
            IConventionEntityTypeBuilder targetEntityTypeBuilder,
            InversePropertyAttribute attribute)
        {
            var entityType = entityTypeBuilder.Metadata;
            var targetClrType = targetEntityTypeBuilder.Metadata.ClrType;
            var inverseNavigationPropertyInfo = targetEntityTypeBuilder.Metadata.GetRuntimeProperties().Values
                .FirstOrDefault(p => string.Equals(p.GetSimpleMemberName(), attribute.Property, StringComparison.OrdinalIgnoreCase));

            if (inverseNavigationPropertyInfo == null
                || !FindCandidateNavigationPropertyType(inverseNavigationPropertyInfo).GetTypeInfo()
                    .IsAssignableFrom(entityType.ClrType.GetTypeInfo()))
            {
                throw new InvalidOperationException(
                    CoreStrings.InvalidNavigationWithInverseProperty(
                        navigationMemberInfo.Name, entityType.DisplayName(), attribute.Property, targetClrType.ShortDisplayName()));
            }

            if (Equals(inverseNavigationPropertyInfo, navigationMemberInfo))
            {
                throw new InvalidOperationException(
                    CoreStrings.SelfReferencingNavigationWithInverseProperty(
                        navigationMemberInfo.Name,
                        entityType.DisplayName(),
                        navigationMemberInfo.Name,
                        entityType.DisplayName()));
            }

            // Check for InversePropertyAttribute on the inverseNavigation to verify that it matches.
            if (Attribute.IsDefined(inverseNavigationPropertyInfo, typeof(InversePropertyAttribute)))
            {
                var inverseAttribute = inverseNavigationPropertyInfo.GetCustomAttribute<InversePropertyAttribute>(true);
                if (inverseAttribute.Property != navigationMemberInfo.GetSimpleMemberName())
                {
                    throw new InvalidOperationException(
                        CoreStrings.InversePropertyMismatch(
                            navigationMemberInfo.Name,
                            entityType.DisplayName(),
                            inverseNavigationPropertyInfo.Name,
                            targetEntityTypeBuilder.Metadata.DisplayName()));
                }
            }

            var referencingNavigationsWithAttribute =
                AddInverseNavigation(entityType, navigationMemberInfo, targetEntityTypeBuilder.Metadata, inverseNavigationPropertyInfo);

            var ambiguousInverse = FindAmbiguousInverse(
                navigationMemberInfo, entityType, referencingNavigationsWithAttribute);
            if (ambiguousInverse != null)
            {
                var existingInverse = targetEntityTypeBuilder.Metadata.FindNavigation(inverseNavigationPropertyInfo)?.FindInverse();
                var existingInverseType = existingInverse?.DeclaringEntityType;
                if (existingInverse != null
                    && IsAmbiguousInverse(
                        existingInverse.GetIdentifyingMemberInfo(), existingInverseType, referencingNavigationsWithAttribute))
                {
                    var fk = existingInverse.ForeignKey;
                    if (fk.IsOwnership
                        || fk.DeclaringEntityType.Builder.HasNoRelationship(fk, fromDataAnnotation: true) == null)
                    {
                        fk.Builder.HasNavigation(
                            (string)null,
                            existingInverse.IsDependentToPrincipal(),
                            fromDataAnnotation: true);
                    }
                }

                var existingNavigation = entityType.FindNavigation(navigationMemberInfo);
                if (existingNavigation != null)
                {
                    var fk = existingNavigation.ForeignKey;
                    if (fk.IsOwnership
                        || fk.DeclaringEntityType.Builder.HasNoRelationship(fk, fromDataAnnotation: true) == null)
                    {
                        fk.Builder.HasNavigation(
                            (string)null,
                            existingNavigation.IsDependentToPrincipal(),
                            fromDataAnnotation: true);
                    }
                }

                var existingAmbiguousNavigation = FindActualEntityType(ambiguousInverse.Value.Item2)
                    .FindNavigation(ambiguousInverse.Value.Item1);
                if (existingAmbiguousNavigation != null)
                {
                    var fk = existingAmbiguousNavigation.ForeignKey;
                    if (fk.IsOwnership
                        || fk.DeclaringEntityType.Builder.HasNoRelationship(fk, fromDataAnnotation: true) == null)
                    {
                        fk.Builder.HasNavigation(
                            (string)null,
                            existingAmbiguousNavigation.IsDependentToPrincipal(),
                            fromDataAnnotation: true);
                    }
                }

                return entityType.FindNavigation(navigationMemberInfo)?.ForeignKey.Builder;
            }

            var ownership = entityType.FindOwnership();
            if (ownership != null
                && ownership.PrincipalEntityType == targetEntityTypeBuilder.Metadata
                && ownership.PrincipalToDependent.GetIdentifyingMemberInfo() != inverseNavigationPropertyInfo)
            {
                Logger.NonOwnershipInverseNavigationWarning(
                    entityType, navigationMemberInfo,
                    targetEntityTypeBuilder.Metadata, inverseNavigationPropertyInfo,
                    ownership.PrincipalToDependent.GetIdentifyingMemberInfo());
                return null;
            }

            if (entityType.DefiningEntityType != null
                && entityType.DefiningEntityType == targetEntityTypeBuilder.Metadata
                && entityType.DefiningNavigationName != inverseNavigationPropertyInfo.GetSimpleMemberName())
            {
                Logger.NonDefiningInverseNavigationWarning(
                    entityType, navigationMemberInfo,
                    targetEntityTypeBuilder.Metadata, inverseNavigationPropertyInfo,
                    entityType.DefiningEntityType.GetRuntimeProperties()[entityType.DefiningNavigationName]);
                return null;
            }

            return entityType.Model.FindIsOwnedConfigurationSource(entityType.ClrType) != null
                   && !entityType.IsInOwnershipPath(targetEntityTypeBuilder.Metadata)
                ? targetEntityTypeBuilder.HasOwnership(
                    entityTypeBuilder.Metadata.ClrType,
                    inverseNavigationPropertyInfo,
                    navigationMemberInfo,
                    fromDataAnnotation: true)
                : targetEntityTypeBuilder.HasRelationship(
                    entityType,
                    inverseNavigationPropertyInfo,
                    navigationMemberInfo,
                    fromDataAnnotation: true);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override void ProcessEntityTypeIgnored(
            IConventionModelBuilder modelBuilder,
            Type type,
            PropertyInfo navigationPropertyInfo,
            Type targetClrType,
            InversePropertyAttribute attribute,
            IConventionContext<string> context)
        {
            var declaringType = navigationPropertyInfo.DeclaringType;
            Debug.Assert(declaringType != null);
            if (modelBuilder.Metadata.FindEntityType(declaringType) != null)
            {
                return;
            }

            var leastDerivedEntityTypes = modelBuilder.Metadata.FindLeastDerivedEntityTypes(
                declaringType,
                t => !t.Builder.IsIgnored(navigationPropertyInfo.GetSimpleMemberName(), fromDataAnnotation: true));
            foreach (var leastDerivedEntityType in leastDerivedEntityTypes)
            {
                ProcessEntityTypeAdded(leastDerivedEntityType.Builder, navigationPropertyInfo, targetClrType, attribute, context);
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override void ProcessNavigationAdded(
            IConventionRelationshipBuilder relationshipBuilder,
            IConventionNavigation navigation,
            InversePropertyAttribute attribute,
            IConventionContext<IConventionNavigation> context)
        {
            if (relationshipBuilder.Metadata.DeclaringEntityType.HasDefiningNavigation()
                || relationshipBuilder.Metadata.DeclaringEntityType.IsOwned()
                || relationshipBuilder.Metadata.PrincipalEntityType.HasDefiningNavigation()
                || relationshipBuilder.Metadata.PrincipalEntityType.IsOwned())
            {
                return;
            }

            var newRelationship = ConfigureInverseNavigation(
                navigation.DeclaringEntityType.Builder,
                navigation.GetIdentifyingMemberInfo(),
                navigation.GetTargetType().Builder,
                attribute);
            if (newRelationship != relationshipBuilder)
            {
                var newNavigation = navigation.IsDependentToPrincipal()
                    ? newRelationship.Metadata.DependentToPrincipal
                    : newRelationship.Metadata.PrincipalToDependent;
                if (newNavigation != navigation)
                {
                    context.StopProcessing(newNavigation);
                }
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override void ProcessEntityTypeBaseTypeChanged(
            IConventionEntityTypeBuilder entityTypeBuilder,
            IConventionEntityType newBaseType1,
            IConventionEntityType oldBaseType,
            MemberInfo navigationMemberInfo,
            Type targetClrType,
            InversePropertyAttribute attribute,
            IConventionContext<IConventionEntityType> context)
        {
            var entityClrType = entityTypeBuilder.Metadata.ClrType;
            if (navigationMemberInfo.DeclaringType != entityClrType)
            {
                var newBaseType = entityTypeBuilder.Metadata.BaseType;
                if (newBaseType == null)
                {
                    ProcessEntityTypeAdded(entityTypeBuilder, navigationMemberInfo, targetClrType, attribute, context);
                }
                else
                {
                    var targetEntityType = entityTypeBuilder.Metadata.Model.FindEntityType(targetClrType);
                    if (targetEntityType == null)
                    {
                        return;
                    }

                    RemoveInverseNavigation(entityTypeBuilder.Metadata, navigationMemberInfo, targetEntityType);
                }
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override void ProcessEntityTypeMemberIgnored(
            IConventionEntityTypeBuilder entityTypeBuilder,
            PropertyInfo navigationPropertyInfo,
            Type targetClrType,
            InversePropertyAttribute attribute,
            IConventionContext<string> context)
        {
            var targetEntityType = RelationshipDiscoveryConvention.GetTargetEntityTypeBuilder(
                entityTypeBuilder, targetClrType, navigationPropertyInfo, null)?.Metadata;
            if (targetEntityType == null)
            {
                return;
            }

            RemoveInverseNavigation(entityTypeBuilder.Metadata, navigationPropertyInfo, targetEntityType);
        }

        /// <summary>
        ///     Called after a model is finalized.
        /// </summary>
        /// <param name="modelBuilder"> The builder for the model. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessModelFinalized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            var model = modelBuilder.Metadata;
            foreach (var entityType in model.GetEntityTypes())
            {
                var inverseNavigations = GetInverseNavigations(entityType);
                if (inverseNavigations == null)
                {
                    continue;
                }

                foreach (var inverseNavigation in inverseNavigations)
                {
                    foreach (var referencingNavigationWithAttribute in inverseNavigation.Value)
                    {
                        var ambiguousInverse = FindAmbiguousInverse(
                            referencingNavigationWithAttribute.Item1,
                            referencingNavigationWithAttribute.Item2,
                            inverseNavigation.Value);
                        if (ambiguousInverse != null)
                        {
                            Logger.MultipleInversePropertiesSameTargetWarning(
                                new[]
                                {
                                    Tuple.Create(
                                        referencingNavigationWithAttribute.Item1, referencingNavigationWithAttribute.Item2.ClrType),
                                    Tuple.Create(ambiguousInverse.Value.Item1, ambiguousInverse.Value.Item2.ClrType)
                                },
                                inverseNavigation.Key,
                                entityType.ClrType);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static bool IsAmbiguous(
            [NotNull] IConventionEntityType entityType, [NotNull] MemberInfo navigation, [NotNull] IConventionEntityType targetEntityType)
        {
            var inverseNavigations = GetInverseNavigations(targetEntityType);
            if (inverseNavigations == null)
            {
                return false;
            }

            foreach (var inverseNavigation in inverseNavigations)
            {
                if (inverseNavigation.Key.GetMemberType().IsAssignableFrom(entityType.ClrType)
                    && IsAmbiguousInverse(navigation, entityType, inverseNavigation.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAmbiguousInverse(
            MemberInfo navigation,
            IConventionEntityType entityType,
            List<(MemberInfo, IConventionEntityType)> referencingNavigationsWithAttribute)
            => FindAmbiguousInverse(navigation, entityType, referencingNavigationsWithAttribute) != null;

        private static (MemberInfo, IConventionEntityType)? FindAmbiguousInverse(
            MemberInfo navigation,
            IConventionEntityType entityType,
            List<(MemberInfo, IConventionEntityType)> referencingNavigationsWithAttribute)
        {
            if (referencingNavigationsWithAttribute.Count == 1)
            {
                return null;
            }

            List<(MemberInfo, IConventionEntityType)> tuplesToRemove = null;
            (MemberInfo, IConventionEntityType)? ambiguousTuple = null;
            foreach (var referencingTuple in referencingNavigationsWithAttribute)
            {
                var inverseTargetEntityType = FindActualEntityType(referencingTuple.Item2);
                if ((inverseTargetEntityType?.Builder.IsIgnored(
                         referencingTuple.Item1.GetSimpleMemberName(), fromDataAnnotation: true) != false))
                {
                    if (tuplesToRemove == null)
                    {
                        tuplesToRemove = new List<(MemberInfo, IConventionEntityType)>();
                    }

                    tuplesToRemove.Add(referencingTuple);
                    continue;
                }

                if (!referencingTuple.Item1.IsSameAs(navigation)
                    || !entityType.IsSameHierarchy(inverseTargetEntityType))
                {
                    ambiguousTuple = referencingTuple;
                    break;
                }
            }

            if (tuplesToRemove != null)
            {
                foreach (var tuple in tuplesToRemove)
                {
                    referencingNavigationsWithAttribute.Remove(tuple);
                }
            }

            return ambiguousTuple;
        }

        private static List<(MemberInfo, IConventionEntityType)> AddInverseNavigation(
            IConventionEntityType entityType, MemberInfo navigation, IConventionEntityType targetEntityType, MemberInfo inverseNavigation)
        {
            var inverseNavigations = GetInverseNavigations(targetEntityType);
            if (inverseNavigations == null)
            {
                inverseNavigations = new Dictionary<MemberInfo, List<(MemberInfo, IConventionEntityType)>>();
                SetInverseNavigations(targetEntityType.Builder, inverseNavigations);
            }

            if (!inverseNavigations.TryGetValue(inverseNavigation, out var referencingNavigationsWithAttribute))
            {
                referencingNavigationsWithAttribute = new List<(MemberInfo, IConventionEntityType)>();
                inverseNavigations[inverseNavigation] = referencingNavigationsWithAttribute;
            }

            foreach (var referencingTuple in referencingNavigationsWithAttribute)
            {
                if (referencingTuple.Item1.IsSameAs(navigation)
                    && referencingTuple.Item2.ClrType == entityType.ClrType
                    && FindActualEntityType(referencingTuple.Item2) == entityType)
                {
                    return referencingNavigationsWithAttribute;
                }
            }

            referencingNavigationsWithAttribute.Add((navigation, entityType));

            return referencingNavigationsWithAttribute;
        }

        private static void RemoveInverseNavigation(
            IConventionEntityType entityType,
            MemberInfo navigation,
            IConventionEntityType targetEntityType)
        {
            var inverseNavigations = GetInverseNavigations(targetEntityType);

            if (inverseNavigations == null)
            {
                return;
            }

            foreach (var inverseNavigationPair in inverseNavigations)
            {
                var inverseNavigation = inverseNavigationPair.Key;
                var referencingNavigationsWithAttribute = inverseNavigationPair.Value;

                for (var index = 0; index < referencingNavigationsWithAttribute.Count; index++)
                {
                    var referencingTuple = referencingNavigationsWithAttribute[index];
                    if (referencingTuple.Item1.IsSameAs(navigation)
                        && referencingTuple.Item2.ClrType == entityType.ClrType
                        && FindActualEntityType(referencingTuple.Item2) == entityType)
                    {
                        referencingNavigationsWithAttribute.RemoveAt(index);
                        if (referencingNavigationsWithAttribute.Count == 0)
                        {
                            inverseNavigations.Remove(inverseNavigation);
                        }

                        if (referencingNavigationsWithAttribute.Count == 1)
                        {
                            var otherEntityType = FindActualEntityType(referencingNavigationsWithAttribute[0].Item2);
                            if (otherEntityType != null)
                            {
                                targetEntityType.Builder.HasRelationship(
                                    otherEntityType,
                                    (PropertyInfo)inverseNavigation,
                                    (PropertyInfo)referencingNavigationsWithAttribute[0].Item1,
                                    fromDataAnnotation: true);
                            }
                        }

                        return;
                    }
                }
            }
        }

        private static IConventionEntityType FindActualEntityType(IConventionEntityType entityType)
            => ((Model)entityType.Model).FindActualEntityType((EntityType) entityType);

        private static Dictionary<MemberInfo, List<(MemberInfo, IConventionEntityType)>> GetInverseNavigations(
            IConventionAnnotatable entityType)
            => entityType.FindAnnotation(InverseNavigationsAnnotationName)?.Value
                as Dictionary<MemberInfo, List<(MemberInfo, IConventionEntityType)>>;

        private static void SetInverseNavigations(
            IConventionAnnotatableBuilder entityTypeBuilder,
            Dictionary<MemberInfo, List<(MemberInfo, IConventionEntityType)>> inverseNavigations)
            => entityTypeBuilder.HasAnnotation(InverseNavigationsAnnotationName, inverseNavigations);
    }
}
