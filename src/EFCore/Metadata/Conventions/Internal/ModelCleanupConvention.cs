// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class ModelCleanupConvention : IModelFinalizedConvention
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public ModelCleanupConvention([NotNull] IDiagnosticsLogger<DbLoggerCategory.Model> logger)
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
        ///     Called after a model is finalized.
        /// </summary>
        /// <param name="modelBuilder"> The builder for the model. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessModelFinalized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            RemoveEntityTypesUnreachableByNavigations(modelBuilder, context);
            RemoveNavigationlessForeignKeys(modelBuilder);
        }

        private void RemoveEntityTypesUnreachableByNavigations(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            var model = modelBuilder.Metadata;
            var rootEntityTypes = GetRoots(model, ConfigurationSource.DataAnnotation);
            using (context.DelayConventions())
            {
                foreach (var orphan in new ModelNavigationsGraphAdapter(model).GetUnreachableVertices(rootEntityTypes))
                {
                    modelBuilder.HasNoEntityType(orphan, fromDataAnnotation: true);
                }
            }
        }

        private IReadOnlyList<IConventionEntityType> GetRoots(IConventionModel model, ConfigurationSource configurationSource)
        {
            var roots = new List<IConventionEntityType>();
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var entityType in model.GetEntityTypes())
            {
                var currentConfigurationSource = entityType.GetConfigurationSource();
                if (currentConfigurationSource.Overrides(configurationSource))
                {
                    roots.Add(entityType);
                }
            }

            return roots;
        }

        private void RemoveNavigationlessForeignKeys(IConventionModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (var foreignKey in entityType.GetDeclaredForeignKeys().ToList())
                {
                    if (foreignKey.PrincipalToDependent == null
                        && foreignKey.DependentToPrincipal == null)
                    {
                        entityType.Builder.HasNoRelationship(foreignKey, fromDataAnnotation: true);
                    }
                }
            }
        }
    }
}
