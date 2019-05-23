﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Cosmos.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.ValueGeneration.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Newtonsoft.Json.Linq;

namespace Microsoft.EntityFrameworkCore.Cosmos.Metadata.Conventions.Internal
{
    public class StoreKeyConvention :
        IEntityTypeAddedConvention,
        IForeignKeyOwnershipChangedConvention,
        IEntityTypeAnnotationChangedConvention,
        IEntityTypeBaseTypeChangedConvention
    {
        public static readonly string IdPropertyName = "id";
        public static readonly string JObjectPropertyName = "__jObject";

        /// <summary>
        ///     Called after an entity type is added to the model.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public void ProcessEntityTypeAdded(
            IConventionEntityTypeBuilder entityTypeBuilder,
            IConventionContext<IConventionEntityTypeBuilder> context)
        {
            var entityType = entityTypeBuilder.Metadata;
            if (entityType.BaseType == null
                && entityType.IsDocumentRoot()
                && !entityType.IsKeyless)
            {
                var idProperty = entityTypeBuilder.Property(typeof(string), IdPropertyName);
                idProperty.HasValueGenerator((_, __) => new IdValueGenerator());
                entityTypeBuilder.HasKey(new[] { idProperty.Metadata });

                var jObjectProperty = entityTypeBuilder.Property(typeof(JObject), JObjectPropertyName);
                jObjectProperty.ForCosmosToProperty("");
                jObjectProperty.ValueGenerated(ValueGenerated.OnAddOrUpdate);
            }
            else
            {
                var idProperty = entityType.FindDeclaredProperty(IdPropertyName);
                if (idProperty != null)
                {
                    var key = entityType.FindKey(idProperty);
                    if (key != null)
                    {
                        entityType.Builder.HasNoKey(key);
                    }
                }

                var jObjectProperty = entityType.FindDeclaredProperty(JObjectPropertyName);
                if (jObjectProperty != null)
                {
                    entityType.Builder.RemoveUnusedShadowProperties(new[] { jObjectProperty });
                }
            }
        }

        /// <summary>
        ///     Called after the ownership value for a foreign key is changed.
        /// </summary>
        /// <param name="relationshipBuilder"> The builder for the foreign key. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public void ProcessForeignKeyOwnershipChanged(
            IConventionRelationshipBuilder relationshipBuilder, IConventionContext<IConventionRelationshipBuilder> context)
        {
            ProcessEntityTypeAdded(relationshipBuilder.Metadata.DeclaringEntityType.Builder, context);
        }

        /// <summary>
        ///     Called after an annotation is changed on an entity type.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type. </param>
        /// <param name="name"> The annotation name. </param>
        /// <param name="annotation"> The new annotation. </param>
        /// <param name="oldAnnotation"> The old annotation.  </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public void ProcessEntityTypeAnnotationChanged(
            IConventionEntityTypeBuilder entityTypeBuilder, string name, IConventionAnnotation annotation,
            IConventionAnnotation oldAnnotation, IConventionContext<IConventionAnnotation> context)
        {
            if (name == CosmosAnnotationNames.ContainerName)
            {
                ProcessEntityTypeAdded(entityTypeBuilder, context);
            }
        }

        /// <summary>
        ///     Called after the base type of an entity type changes.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type. </param>
        /// <param name="newBaseType"> The new base entity type. </param>
        /// <param name="oldBaseType"> The old base entity type. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public void ProcessEntityTypeBaseTypeChanged(
            IConventionEntityTypeBuilder entityTypeBuilder,
            IConventionEntityType newBaseType,
            IConventionEntityType oldBaseType,
            IConventionContext<IConventionEntityType> context)
        {
            ProcessForeignKeyOwnershipChanged(entityTypeBuilder, context);
        }
    }
}
