using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MediaPortal.Plugins.BDHandler.Filters
{
    /// <summary>
    /// Common interface for filters
    /// </summary>
    public interface IFilter
    {
        /// <summary>
        /// Gets the name of the filter
        /// </summary>
        /// <value>Name of the filter</value>
        string Name { get; }

        /// <summary>
        /// Gets the GUID of the filter
        /// </summary>
        /// <value>The GUID.</value>
        Guid GUID { get; }

        /// <summary>
        /// Gets the recommended build number for this filter.
        /// </summary>
        /// <value>The recommended build number.</value>
        int RecommendedBuildNumber { get; }

    }
}
