﻿using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;

namespace Emby.Server.Implementations.Library
{
    /// <summary>
    /// </summary>
    public class SearchEngine : ISearchEngine
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public SearchEngine(ILogManager logManager, ILibraryManager libraryManager, IUserManager userManager)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;

            _logger = logManager.GetLogger("SearchEngine");
        }

        public QueryResult<SearchHintInfo> GetSearchHints(SearchQuery query)
        {
            User user = null;

            if (string.IsNullOrEmpty(query.UserId))
            {
            }
            else
            {
                user = _userManager.GetUserById(query.UserId);
            }

            var results = GetSearchHints(query, user);
            var totalRecordCount = results.Count;

            if (query.StartIndex.HasValue)
            {
                results = results.Skip(query.StartIndex.Value).ToList();
            }

            if (query.Limit.HasValue)
            {
                results = results.Take(query.Limit.Value).ToList();
            }

            return new QueryResult<SearchHintInfo>
            {
                TotalRecordCount = totalRecordCount,

                Items = results.ToArray()
            };
        }

        private void AddIfMissing(List<string> list, string value)
        {
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(value);
            }
        }

        /// <summary>
        /// Gets the search hints.
        /// </summary>
        /// <param name="query">The query.</param>
        /// <param name="user">The user.</param>
        /// <returns>IEnumerable{SearchHintResult}.</returns>
        /// <exception cref="System.ArgumentNullException">searchTerm</exception>
        private List<SearchHintInfo> GetSearchHints(SearchQuery query, User user)
        {
            var searchTerm = query.SearchTerm;

            if (string.IsNullOrEmpty(searchTerm))
            {
                throw new ArgumentNullException("searchTerm");
            }

            searchTerm = searchTerm.Trim().RemoveDiacritics();

            searchTerm = this.FixUnicodeChars(searchTerm);

            var terms = GetWords(searchTerm);

            var excludeItemTypes = query.ExcludeItemTypes.ToList();
            var includeItemTypes = (query.IncludeItemTypes ?? Array.Empty<string>()).ToList();

            excludeItemTypes.Add(typeof(Year).Name);
            excludeItemTypes.Add(typeof(Folder).Name);

            if (query.IncludeGenres && (includeItemTypes.Count == 0 || includeItemTypes.Contains("Genre", StringComparer.OrdinalIgnoreCase)))
            {
                if (!query.IncludeMedia)
                {
                    AddIfMissing(includeItemTypes, typeof(Genre).Name);
                    AddIfMissing(includeItemTypes, typeof(GameGenre).Name);
                    AddIfMissing(includeItemTypes, typeof(MusicGenre).Name);
                }
            }
            else
            {
                AddIfMissing(excludeItemTypes, typeof(Genre).Name);
                AddIfMissing(excludeItemTypes, typeof(GameGenre).Name);
                AddIfMissing(excludeItemTypes, typeof(MusicGenre).Name);
            }

            if (query.IncludePeople && (includeItemTypes.Count == 0 || includeItemTypes.Contains("People", StringComparer.OrdinalIgnoreCase) || includeItemTypes.Contains("Person", StringComparer.OrdinalIgnoreCase)))
            {
                if (!query.IncludeMedia)
                {
                    AddIfMissing(includeItemTypes, typeof(Person).Name);
                }
            }
            else
            {
                AddIfMissing(excludeItemTypes, typeof(Person).Name);
            }

            if (query.IncludeStudios && (includeItemTypes.Count == 0 || includeItemTypes.Contains("Studio", StringComparer.OrdinalIgnoreCase)))
            {
                if (!query.IncludeMedia)
                {
                    AddIfMissing(includeItemTypes, typeof(Studio).Name);
                }
            }
            else
            {
                AddIfMissing(excludeItemTypes, typeof(Studio).Name);
            }

            if (query.IncludeArtists && (includeItemTypes.Count == 0 || includeItemTypes.Contains("MusicArtist", StringComparer.OrdinalIgnoreCase)))
            {
                if (!query.IncludeMedia)
                {
                    AddIfMissing(includeItemTypes, typeof(MusicArtist).Name);
                }
            }
            else
            {
                AddIfMissing(excludeItemTypes, typeof(MusicArtist).Name);
            }

            AddIfMissing(excludeItemTypes, typeof(CollectionFolder).Name);
            AddIfMissing(excludeItemTypes, typeof(Folder).Name);
            var mediaTypes = query.MediaTypes.ToList();

            if (includeItemTypes.Count > 0)
            {
                excludeItemTypes.Clear();
                mediaTypes.Clear();
            }

            var searchQuery = new InternalItemsQuery(user)
            {
                NameContains = searchTerm,
                ExcludeItemTypes = excludeItemTypes.ToArray(excludeItemTypes.Count),
                IncludeItemTypes = includeItemTypes.ToArray(includeItemTypes.Count),
                Limit = query.Limit,
                IncludeItemsByName = string.IsNullOrEmpty(query.ParentId),
                ParentId = string.IsNullOrEmpty(query.ParentId) ? (Guid?)null : new Guid(query.ParentId),
                OrderBy = new[] { new Tuple<string, SortOrder>(ItemSortBy.SortName, SortOrder.Ascending) },
                Recursive = true,

                IsKids = query.IsKids,
                IsMovie = query.IsMovie,
                IsNews = query.IsNews,
                IsSeries = query.IsSeries,
                IsSports = query.IsSports,
                MediaTypes = mediaTypes.ToArray(),

                DtoOptions = new DtoOptions
                {
                    Fields = new ItemFields[]
                    {
                         ItemFields.AirTime,
                         ItemFields.DateCreated,
                         ItemFields.ChannelInfo,
                         ItemFields.ParentId
                    }
                }
            };

            List<BaseItem> mediaItems;

            if (searchQuery.IncludeItemTypes.Length == 1 && string.Equals(searchQuery.IncludeItemTypes[0], "MusicArtist", StringComparison.OrdinalIgnoreCase))
            {
                if (searchQuery.ParentId.HasValue)
                {
                    searchQuery.AncestorIds = new Guid[] { searchQuery.ParentId.Value };
                }
                searchQuery.ParentId = null;
                searchQuery.IncludeItemsByName = true;
                searchQuery.IncludeItemTypes = Array.Empty<string>();
                mediaItems = _libraryManager.GetAllArtists(searchQuery).Items.Select(i => i.Item1).ToList();
            }
            else
            {
                mediaItems = _libraryManager.GetItemList(searchQuery);
            }

            var returnValue = mediaItems.Select(item =>
            {
                var index = GetIndex(item.Name, searchTerm, terms);

                return new Tuple<BaseItem, string, int>(item, index.Item1, index.Item2);

            }).OrderBy(i => i.Item3).ThenBy(i => i.Item1.SortName).Select(i => new SearchHintInfo
            {
                Item = i.Item1,
                MatchedTerm = i.Item2

            }).ToList();

            return returnValue;
        }

        /// <summary>
        /// Gets the index.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <param name="searchInput">The search input.</param>
        /// <param name="searchWords">The search input.</param>
        /// <returns>System.Int32.</returns>
        private Tuple<string, int> GetIndex(string input, string searchInput, List<string> searchWords)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new ArgumentNullException("input");
            }

            input = input.RemoveDiacritics();

            if (string.Equals(input, searchInput, StringComparison.OrdinalIgnoreCase))
            {
                return new Tuple<string, int>(searchInput, 0);
            }

            var index = input.IndexOf(searchInput, StringComparison.OrdinalIgnoreCase);

            if (index == 0)
            {
                return new Tuple<string, int>(searchInput, 1);
            }
            if (index > 0)
            {
                return new Tuple<string, int>(searchInput, 2);
            }

            var items = GetWords(input);

            for (var i = 0; i < searchWords.Count; i++)
            {
                var searchTerm = searchWords[i];

                for (var j = 0; j < items.Count; j++)
                {
                    var item = items[j];

                    if (string.Equals(item, searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        return new Tuple<string, int>(searchTerm, 3 + (i + 1) * (j + 1));
                    }

                    index = item.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);

                    if (index == 0)
                    {
                        return new Tuple<string, int>(searchTerm, 4 + (i + 1) * (j + 1));
                    }
                    if (index > 0)
                    {
                        return new Tuple<string, int>(searchTerm, 5 + (i + 1) * (j + 1));
                    }
                }
            }
            return new Tuple<string, int>(null, -1);
        }

        /// <summary>
        /// Gets the words.
        /// </summary>
        /// <param name="term">The term.</param>
        /// <returns>System.String[][].</returns>
        private List<string> GetWords(string term)
        {
            var stoplist = GetStopList().ToList();

            return term.Split()
                .Where(i => !string.IsNullOrWhiteSpace(i) && !stoplist.Contains(i, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        private string FixUnicodeChars(string buffer)
        {
            if (buffer.IndexOf('\u2013') > -1) buffer = buffer.Replace('\u2013', '-'); // en dash
            if (buffer.IndexOf('\u2014') > -1) buffer = buffer.Replace('\u2014', '-'); // em dash
            if (buffer.IndexOf('\u2015') > -1) buffer = buffer.Replace('\u2015', '-'); // horizontal bar
            if (buffer.IndexOf('\u2017') > -1) buffer = buffer.Replace('\u2017', '_'); // double low line
            if (buffer.IndexOf('\u2018') > -1) buffer = buffer.Replace('\u2018', '\''); // left single quotation mark
            if (buffer.IndexOf('\u2019') > -1) buffer = buffer.Replace('\u2019', '\''); // right single quotation mark
            if (buffer.IndexOf('\u201a') > -1) buffer = buffer.Replace('\u201a', ','); // single low-9 quotation mark
            if (buffer.IndexOf('\u201b') > -1) buffer = buffer.Replace('\u201b', '\''); // single high-reversed-9 quotation mark
            if (buffer.IndexOf('\u201c') > -1) buffer = buffer.Replace('\u201c', '\"'); // left double quotation mark
            if (buffer.IndexOf('\u201d') > -1) buffer = buffer.Replace('\u201d', '\"'); // right double quotation mark
            if (buffer.IndexOf('\u201e') > -1) buffer = buffer.Replace('\u201e', '\"'); // double low-9 quotation mark
            if (buffer.IndexOf('\u2026') > -1) buffer = buffer.Replace("\u2026", "..."); // horizontal ellipsis
            if (buffer.IndexOf('\u2032') > -1) buffer = buffer.Replace('\u2032', '\''); // prime
            if (buffer.IndexOf('\u2033') > -1) buffer = buffer.Replace('\u2033', '\"'); // double prime
            if (buffer.IndexOf('\u0060') > -1) buffer = buffer.Replace('\u0060', '\''); // grave accent
            if (buffer.IndexOf('\u00B4') > -1) buffer = buffer.Replace('\u00B4', '\''); // acute accent

            return buffer;
        }

        private IEnumerable<string> GetStopList()
        {
            return new[]
            {
                "the",
                "a",
                "of",
                "an"
            };
        }
    }
}
