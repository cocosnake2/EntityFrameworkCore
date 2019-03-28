// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Extension methods for <see cref="IMutableForeignKey" />.
    /// </summary>
    public static class MutableForeignKeyExtensions
    {
        /// <summary>
        ///     Gets the entity type related to the given one.
        /// </summary>
        /// <param name="foreignKey"> The foreign key. </param>
        /// <param name="entityType"> One of the entity types related by the foreign key. </param>
        /// <returns> The entity type related to the given one. </returns>
        public static IMutableEntityType ResolveOtherEntityType(
            [NotNull] this IMutableForeignKey foreignKey, [NotNull] IMutableEntityType entityType)
            => (IMutableEntityType)((IForeignKey)foreignKey).ResolveOtherEntityType(entityType);
    }
}
