// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions
{
    /// <summary>
    ///     Contextual information associated with each convention call.
    /// </summary>
    /// <typeparam name="T"> The type of the metadata object. </typeparam>
    public interface IConventionContext<T> : IConventionContext
    {
        /// <summary>
        ///     <para>
        ///         Calling this will prevent further processing of the associated event by other conventions.
        ///     </para>
        ///     <para>
        ///         The common use case is when the metadata object was replaced by the convention.
        ///     </para>
        /// </summary>
        /// <param name="result"> The new metadata object. </param>
        void StopProcessing(T result);
    }
}
