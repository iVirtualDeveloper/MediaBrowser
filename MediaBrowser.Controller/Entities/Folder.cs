﻿using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Progress;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Localization;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;
using MoreLinq;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Entities
{
    /// <summary>
    /// Class Folder
    /// </summary>
    public class Folder : BaseItem, IHasThemeMedia
    {
        public static IUserManager UserManager { get; set; }

        public List<Guid> ThemeSongIds { get; set; }
        public List<Guid> ThemeVideoIds { get; set; }
        
        public Folder()
        {
            LinkedChildren = new List<LinkedChild>();

            ThemeSongIds = new List<Guid>();
            ThemeVideoIds = new List<Guid>();
        }

        /// <summary>
        /// Gets a value indicating whether this instance is folder.
        /// </summary>
        /// <value><c>true</c> if this instance is folder; otherwise, <c>false</c>.</value>
        [IgnoreDataMember]
        public override bool IsFolder
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is physical root.
        /// </summary>
        /// <value><c>true</c> if this instance is physical root; otherwise, <c>false</c>.</value>
        public bool IsPhysicalRoot { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether this instance is root.
        /// </summary>
        /// <value><c>true</c> if this instance is root; otherwise, <c>false</c>.</value>
        public bool IsRoot { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is virtual folder.
        /// </summary>
        /// <value><c>true</c> if this instance is virtual folder; otherwise, <c>false</c>.</value>
        [IgnoreDataMember]
        public virtual bool IsVirtualFolder
        {
            get
            {
                return false;
            }
        }

        public virtual List<LinkedChild> LinkedChildren { get; set; }

        protected virtual bool SupportsShortcutChildren
        {
            get { return true; }
        }

        /// <summary>
        /// Adds the child.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.InvalidOperationException">Unable to add  + item.Name</exception>
        public async Task AddChild(BaseItem item, CancellationToken cancellationToken)
        {
            item.Parent = this;

            if (item.Id == Guid.Empty)
            {
                item.Id = item.Path.GetMBId(item.GetType());
            }

            if (ActualChildren.Any(i => i.Id == item.Id))
            {
                throw new ArgumentException(string.Format("A child with the Id {0} already exists.", item.Id));
            }

            if (item.DateCreated == DateTime.MinValue)
            {
                item.DateCreated = DateTime.UtcNow;
            }
            if (item.DateModified == DateTime.MinValue)
            {
                item.DateModified = DateTime.UtcNow;
            }

            AddChildInternal(item);

            await LibraryManager.CreateItem(item, cancellationToken).ConfigureAwait(false);

            await ItemRepository.SaveChildren(Id, ActualChildren.Select(i => i.Id).ToList(), cancellationToken).ConfigureAwait(false);
        }

        protected void AddChildrenInternal(IEnumerable<BaseItem> children)
        {
            lock (_childrenSyncLock)
            {
                var newChildren = ActualChildren.ToList();
                newChildren.AddRange(children);
                _children = newChildren;
            }
        }
        protected void AddChildInternal(BaseItem child)
        {
            lock (_childrenSyncLock)
            {
                var newChildren = ActualChildren.ToList();
                newChildren.Add(child);
                _children = newChildren;
            }
        }

        protected void RemoveChildrenInternal(IEnumerable<BaseItem> children)
        {
            lock (_childrenSyncLock)
            {
                _children = ActualChildren.Except(children).ToList();
            }
        }

        protected void ClearChildrenInternal()
        {
            lock (_childrenSyncLock)
            {
                _children = new List<BaseItem>();
            }
        }

        /// <summary>
        /// Never want folders to be blocked by "BlockNotRated"
        /// </summary>
        [IgnoreDataMember]
        public override string OfficialRatingForComparison
        {
            get
            {
                if (this is Series)
                {
                    return base.OfficialRatingForComparison;
                }

                return !string.IsNullOrEmpty(base.OfficialRatingForComparison) ? base.OfficialRatingForComparison : "None";
            }
        }

        /// <summary>
        /// Removes the child.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.InvalidOperationException">Unable to remove  + item.Name</exception>
        public Task RemoveChild(BaseItem item, CancellationToken cancellationToken)
        {
            RemoveChildrenInternal(new[] { item });

            item.Parent = null;

            LibraryManager.ReportItemRemoved(item);

            return ItemRepository.SaveChildren(Id, ActualChildren.Select(i => i.Id).ToList(), cancellationToken);
        }

        /// <summary>
        /// Clears the children.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task ClearChildren(CancellationToken cancellationToken)
        {
            var items = ActualChildren.ToList();

            ClearChildrenInternal();

            foreach (var item in items)
            {
                LibraryManager.ReportItemRemoved(item);
            }

            return ItemRepository.SaveChildren(Id, ActualChildren.Select(i => i.Id).ToList(), cancellationToken);
        }

        #region Indexing

        /// <summary>
        /// Returns the valid set of index by options for this folder type.
        /// Override or extend to modify.
        /// </summary>
        /// <returns>Dictionary{System.StringFunc{UserIEnumerable{BaseItem}}}.</returns>
        protected virtual IEnumerable<string> GetIndexByOptions()
        {
            return new List<string> {            
                {LocalizedStrings.Instance.GetString("NoneDispPref")}, 
                {LocalizedStrings.Instance.GetString("PerformerDispPref")},
                {LocalizedStrings.Instance.GetString("GenreDispPref")},
                {LocalizedStrings.Instance.GetString("DirectorDispPref")},
                {LocalizedStrings.Instance.GetString("YearDispPref")},
                //{LocalizedStrings.Instance.GetString("OfficialRatingDispPref"), null},
                {LocalizedStrings.Instance.GetString("StudioDispPref")}
            };

        }

        /// <summary>
        /// Get the list of indexy by choices for this folder (localized).
        /// </summary>
        /// <value>The index by option strings.</value>
        [IgnoreDataMember]
        public IEnumerable<string> IndexByOptionStrings
        {
            get { return GetIndexByOptions(); }
        }

        #endregion

        /// <summary>
        /// The children
        /// </summary>
        private IReadOnlyList<BaseItem> _children;
        /// <summary>
        /// The _children sync lock
        /// </summary>
        private readonly object _childrenSyncLock = new object();
        /// <summary>
        /// Gets or sets the actual children.
        /// </summary>
        /// <value>The actual children.</value>
        protected virtual IEnumerable<BaseItem> ActualChildren
        {
            get
            {
                return _children ?? (_children = LoadChildrenInternal());
            }
        }

        /// <summary>
        /// thread-safe access to the actual children of this folder - without regard to user
        /// </summary>
        /// <value>The children.</value>
        [IgnoreDataMember]
        public IEnumerable<BaseItem> Children
        {
            get { return ActualChildren; }
        }

        /// <summary>
        /// thread-safe access to all recursive children of this folder - without regard to user
        /// </summary>
        /// <value>The recursive children.</value>
        [IgnoreDataMember]
        public IEnumerable<BaseItem> RecursiveChildren
        {
            get { return GetRecursiveChildren(); }
        }

        private List<BaseItem> LoadChildrenInternal()
        {
            return LoadChildren().ToList();
        }

        /// <summary>
        /// Loads our children.  Validation will occur externally.
        /// We want this sychronous.
        /// </summary>
        protected virtual IEnumerable<BaseItem> LoadChildren()
        {
            //just load our children from the repo - the library will be validated and maintained in other processes
            return GetCachedChildren();
        }

        /// <summary>
        /// Gets or sets the current validation cancellation token source.
        /// </summary>
        /// <value>The current validation cancellation token source.</value>
        private CancellationTokenSource CurrentValidationCancellationTokenSource { get; set; }

        /// <summary>
        /// Validates that the children of the folder still exist
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="forceRefreshMetadata">if set to <c>true</c> [force refresh metadata].</param>
        /// <returns>Task.</returns>
        public async Task ValidateChildren(IProgress<double> progress, CancellationToken cancellationToken, bool? recursive = null, bool forceRefreshMetadata = false)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Cancel the current validation, if any
            if (CurrentValidationCancellationTokenSource != null)
            {
                CurrentValidationCancellationTokenSource.Cancel();
            }

            // Create an inner cancellation token. This can cancel all validations from this level on down,
            // but nothing above this
            var innerCancellationTokenSource = new CancellationTokenSource();

            try
            {
                CurrentValidationCancellationTokenSource = innerCancellationTokenSource;

                var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(innerCancellationTokenSource.Token, cancellationToken);

                await ValidateChildrenInternal(progress, linkedCancellationTokenSource.Token, recursive, forceRefreshMetadata).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                Logger.Info("ValidateChildren cancelled for " + Name);

                // If the outer cancelletion token in the cause for the cancellation, throw it
                if (cancellationToken.IsCancellationRequested && ex.CancellationToken == cancellationToken)
                {
                    throw;
                }
            }
            finally
            {
                // Null out the token source             
                if (CurrentValidationCancellationTokenSource == innerCancellationTokenSource)
                {
                    CurrentValidationCancellationTokenSource = null;
                }

                innerCancellationTokenSource.Dispose();
            }
        }

        /// <summary>
        /// Compare our current children (presumably just read from the repo) with the current state of the file system and adjust for any changes
        /// ***Currently does not contain logic to maintain items that are unavailable in the file system***
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="forceRefreshMetadata">if set to <c>true</c> [force refresh metadata].</param>
        /// <returns>Task.</returns>
        protected async virtual Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool? recursive = null, bool forceRefreshMetadata = false)
        {
            var locationType = LocationType;

            // Nothing to do here
            if (locationType == LocationType.Remote || locationType == LocationType.Virtual)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<BaseItem> nonCachedChildren;

            try
            {
                nonCachedChildren = GetNonCachedChildren();
            }
            catch (IOException ex)
            {
                nonCachedChildren = new BaseItem[] { };

                Logger.ErrorException("Error getting file system entries for {0}", ex, Path);
            }

            if (nonCachedChildren == null) return; //nothing to validate

            progress.Report(5);

            //build a dictionary of the current children we have now by Id so we can compare quickly and easily
            var currentChildren = ActualChildren.ToDictionary(i => i.Id);

            //create a list for our validated children
            var validChildren = new List<Tuple<BaseItem, bool>>();
            var newItems = new List<BaseItem>();

            cancellationToken.ThrowIfCancellationRequested();

            foreach (var child in nonCachedChildren)
            {
                BaseItem currentChild;

                if (currentChildren.TryGetValue(child.Id, out currentChild))
                {
                    currentChild.ResetResolveArgs(child.ResolveArgs);

                    //existing item - check if it has changed
                    if (currentChild.HasChanged(child))
                    {
                        var currentChildLocationType = currentChild.LocationType;
                        if (currentChildLocationType != LocationType.Remote &&
                            currentChildLocationType != LocationType.Virtual)
                        {
                            EntityResolutionHelper.EnsureDates(FileSystem, currentChild, child.ResolveArgs, false);
                        }

                        validChildren.Add(new Tuple<BaseItem, bool>(currentChild, true));
                    }
                    else
                    {
                        validChildren.Add(new Tuple<BaseItem, bool>(currentChild, false));
                    }

                    currentChild.IsOffline = false;
                }
                else
                {
                    //brand new item - needs to be added
                    newItems.Add(child);

                    validChildren.Add(new Tuple<BaseItem, bool>(child, true));
                }
            }

            // If any items were added or removed....
            if (newItems.Count > 0 || currentChildren.Count != validChildren.Count)
            {
                var newChildren = validChildren.Select(c => c.Item1).ToList();

                // That's all the new and changed ones - now see if there are any that are missing
                var itemsRemoved = currentChildren.Values.Except(newChildren).ToList();

                var actualRemovals = new List<BaseItem>();

                foreach (var item in itemsRemoved)
                {
                    if (item.LocationType == LocationType.Virtual ||
                        item.LocationType == LocationType.Remote)
                    {
                        // Don't remove these because there's no way to accurately validate them.
                        continue;
                    }
                    
                    if (!string.IsNullOrEmpty(item.Path) && IsPathOffline(item.Path))
                    {
                        item.IsOffline = true;

                        validChildren.Add(new Tuple<BaseItem, bool>(item, false));
                    }
                    else
                    {
                        item.IsOffline = false;
                        actualRemovals.Add(item);
                    }
                }

                if (actualRemovals.Count > 0)
                {
                    RemoveChildrenInternal(actualRemovals);

                    foreach (var item in actualRemovals)
                    {
                        LibraryManager.ReportItemRemoved(item);
                    }
                }

                await LibraryManager.CreateItems(newItems, cancellationToken).ConfigureAwait(false);

                AddChildrenInternal(newItems);

                await ItemRepository.SaveChildren(Id, ActualChildren.Select(i => i.Id).ToList(), cancellationToken).ConfigureAwait(false);
            }

            progress.Report(10);

            cancellationToken.ThrowIfCancellationRequested();

            await RefreshChildren(validChildren, progress, cancellationToken, recursive, forceRefreshMetadata).ConfigureAwait(false);

            progress.Report(100);
        }

        /// <summary>
        /// Refreshes the children.
        /// </summary>
        /// <param name="children">The children.</param>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="forceRefreshMetadata">if set to <c>true</c> [force refresh metadata].</param>
        /// <returns>Task.</returns>
        private async Task RefreshChildren(IList<Tuple<BaseItem, bool>> children, IProgress<double> progress, CancellationToken cancellationToken, bool? recursive, bool forceRefreshMetadata = false)
        {
            var list = children;

            var percentages = new Dictionary<Guid, double>(list.Count);

            var tasks = new List<Task>();

            foreach (var tuple in list)
            {
                if (tasks.Count > 10)
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                Tuple<BaseItem, bool> currentTuple = tuple;

                tasks.Add(Task.Run(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var child = currentTuple.Item1;
                    try
                    {
                        //refresh it
                        await child.RefreshMetadata(cancellationToken, forceSave: currentTuple.Item2, forceRefresh: forceRefreshMetadata, resetResolveArgs: false).ConfigureAwait(false);
                    }
                    catch (IOException ex)
                    {
                        Logger.ErrorException("Error refreshing {0}", ex, child.Path ?? child.Name);
                    }

                    // Refresh children if a folder and the item changed or recursive is set to true
                    var refreshChildren = child.IsFolder && (currentTuple.Item2 || (recursive.HasValue && recursive.Value));

                    if (refreshChildren)
                    {
                        // Don't refresh children if explicitly set to false
                        if (recursive.HasValue && recursive.Value == false)
                        {
                            refreshChildren = false;
                        }
                    }

                    if (refreshChildren)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var innerProgress = new ActionableProgress<double>();

                        innerProgress.RegisterAction(p =>
                        {
                            lock (percentages)
                            {
                                percentages[child.Id] = p / 100;

                                var percent = percentages.Values.Sum();
                                percent /= list.Count;

                                progress.Report((90 * percent) + 10);
                            }
                        });

                        await ((Folder)child).ValidateChildren(innerProgress, cancellationToken, recursive, forceRefreshMetadata).ConfigureAwait(false);

                        try
                        {
                            // Some folder providers are unable to refresh until children have been refreshed.
                            await child.RefreshMetadata(cancellationToken, resetResolveArgs: false).ConfigureAwait(false);
                        }
                        catch (IOException ex)
                        {
                            Logger.ErrorException("Error refreshing {0}", ex, child.Path ?? child.Name);
                        }
                    }
                    else
                    {
                        lock (percentages)
                        {
                            percentages[child.Id] = 1;

                            var percent = percentages.Values.Sum();
                            percent /= list.Count;

                            progress.Report((90 * percent) + 10);
                        }
                    }

                }, cancellationToken));
            }

            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Determines whether the specified path is offline.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if the specified path is offline; otherwise, <c>false</c>.</returns>
        private bool IsPathOffline(string path)
        {
            if (File.Exists(path))
            {
                return false;
            }

            var originalPath = path;

            // Depending on whether the path is local or unc, it may return either null or '\' at the top
            while (!string.IsNullOrEmpty(path) && path.Length > 1)
            {
                if (Directory.Exists(path))
                {
                    return false;
                }

                path = System.IO.Path.GetDirectoryName(path);
            }

            if (ContainsPath(LibraryManager.GetDefaultVirtualFolders(), originalPath))
            {
                return true;
            }

            return UserManager.Users.Any(user => ContainsPath(LibraryManager.GetVirtualFolders(user), originalPath));
        }

        /// <summary>
        /// Determines whether the specified folders contains path.
        /// </summary>
        /// <param name="folders">The folders.</param>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if the specified folders contains path; otherwise, <c>false</c>.</returns>
        private bool ContainsPath(IEnumerable<VirtualFolderInfo> folders, string path)
        {
            return folders.SelectMany(i => i.Locations).Any(i => ContainsPath(i, path));
        }

        private bool ContainsPath(string parent, string path)
        {
            return string.Equals(parent, path, StringComparison.OrdinalIgnoreCase) || path.IndexOf(parent.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) != -1;
        }

        /// <summary>
        /// Get the children of this folder from the actual file system
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected virtual IEnumerable<BaseItem> GetNonCachedChildren()
        {
            var resolveArgs = ResolveArgs;

            if (resolveArgs == null || resolveArgs.FileSystemDictionary == null)
            {
                Logger.Error("ResolveArgs null for {0}", Path);
            }

            return LibraryManager.ResolvePaths<BaseItem>(resolveArgs.FileSystemChildren, this);
        }

        /// <summary>
        /// Get our children from the repo - stubbed for now
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected IEnumerable<BaseItem> GetCachedChildren()
        {
            return ItemRepository.GetChildren(Id).Select(RetrieveChild).Where(i => i != null);
        }

        /// <summary>
        /// Retrieves the child.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <returns>BaseItem.</returns>
        private BaseItem RetrieveChild(Guid child)
        {
            var item = LibraryManager.RetrieveItem(child);

            if (item != null)
            {
                if (item is IByReferenceItem)
                {
                    return LibraryManager.GetOrAddByReferenceItem(item);
                }

                item.Parent = this;
            }

            return item;
        }

        /// <summary>
        /// Gets allowed children of an item
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="includeLinkedChildren">if set to <c>true</c> [include linked children].</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public virtual IEnumerable<BaseItem> GetChildren(User user, bool includeLinkedChildren)
        {
            if (user == null)
            {
                throw new ArgumentNullException();
            }

            //the true root should return our users root folder children
            if (IsPhysicalRoot) return user.RootFolder.GetChildren(user, includeLinkedChildren);

            var list = new List<BaseItem>();

            AddChildrenToList(user, includeLinkedChildren, list, false, null);

            return list;
        }

        /// <summary>
        /// Adds the children to list.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="includeLinkedChildren">if set to <c>true</c> [include linked children].</param>
        /// <param name="list">The list.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="filter">The filter.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        private bool AddChildrenToList(User user, bool includeLinkedChildren, List<BaseItem> list, bool recursive, Func<BaseItem, bool> filter)
        {
            var hasLinkedChildren = false;

            foreach (var child in Children)
            {
                if (child.IsVisible(user))
                {
                    if (filter == null || filter(child))
                    {
                        list.Add(child);
                    }
                }

                if (recursive && child.IsFolder)
                {
                    var folder = (Folder)child;

                    if (folder.AddChildrenToList(user, includeLinkedChildren, list, true, filter))
                    {
                        hasLinkedChildren = true;
                    }
                }
            }

            if (includeLinkedChildren)
            {
                foreach (var child in GetLinkedChildren())
                {
                    if (filter != null && !filter(child))
                    {
                        continue;
                    }

                    if (child.IsVisible(user))
                    {
                        hasLinkedChildren = true;

                        list.Add(child);
                    }
                }
            }

            return hasLinkedChildren;
        }

        /// <summary>
        /// Gets allowed recursive children of an item
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="includeLinkedChildren">if set to <c>true</c> [include linked children].</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public IEnumerable<BaseItem> GetRecursiveChildren(User user, bool includeLinkedChildren = true)
        {
            return GetRecursiveChildren(user, null, includeLinkedChildren);
        }

        /// <summary>
        /// Gets the recursive children.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="includeLinkedChildren">if set to <c>true</c> [include linked children].</param>
        /// <returns>IList{BaseItem}.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public IList<BaseItem> GetRecursiveChildren(User user, Func<BaseItem, bool> filter, bool includeLinkedChildren = true)
        {
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var list = new List<BaseItem>();

            var hasLinkedChildren = AddChildrenToList(user, includeLinkedChildren, list, true, filter);

            return hasLinkedChildren ? list.DistinctBy(i => i.Id).ToList() : list;
        }

        /// <summary>
        /// Gets the recursive children.
        /// </summary>
        /// <returns>IList{BaseItem}.</returns>
        public IList<BaseItem> GetRecursiveChildren()
        {
            return GetRecursiveChildren(i => true);
        }

        /// <summary>
        /// Gets the recursive children.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        public IList<BaseItem> GetRecursiveChildren(Func<BaseItem, bool> filter)
        {
            var list = new List<BaseItem>();

            AddChildrenToList(list, true, filter);

            return list;
        }

        /// <summary>
        /// Adds the children to list.
        /// </summary>
        /// <param name="list">The list.</param>
        /// <param name="recursive">if set to <c>true</c> [recursive].</param>
        /// <param name="filter">The filter.</param>
        private void AddChildrenToList(List<BaseItem> list, bool recursive, Func<BaseItem, bool> filter)
        {
            foreach (var child in Children)
            {
                if (filter == null || filter(child))
                {
                    list.Add(child);
                }

                if (recursive && child.IsFolder)
                {
                    var folder = (Folder)child;

                    folder.AddChildrenToList(list, true, filter);
                }
            }
        }


        /// <summary>
        /// Gets the linked children.
        /// </summary>
        /// <returns>IEnumerable{BaseItem}.</returns>
        public IEnumerable<BaseItem> GetLinkedChildren()
        {
            return LinkedChildren
                .Select(GetLinkedChild)
                .Where(i => i != null);
        }

        /// <summary>
        /// Gets the linked child.
        /// </summary>
        /// <param name="info">The info.</param>
        /// <returns>BaseItem.</returns>
        private BaseItem GetLinkedChild(LinkedChild info)
        {
            if (string.IsNullOrEmpty(info.Path))
            {
                throw new ArgumentException("Encountered linked child with empty path.");
            }

            BaseItem item = null;

            // First get using the cached Id
            if (info.ItemId != Guid.Empty)
            {
                item = LibraryManager.GetItemById(info.ItemId);
            }

            // If still null, search by path
            if (item == null)
            {
                item = LibraryManager.RootFolder.FindByPath(info.Path);
            }

            // If still null, log
            if (item == null)
            {
                Logger.Warn("Unable to find linked item at {0}", info.Path);
            }
            else
            {
                // Cache the id for next time
                info.ItemId = item.Id;
            }

            return item;
        }

        public override async Task<bool> RefreshMetadata(CancellationToken cancellationToken, bool forceSave = false, bool forceRefresh = false, bool allowSlowProviders = true, bool resetResolveArgs = true)
        {
            var changed = await base.RefreshMetadata(cancellationToken, forceSave, forceRefresh, allowSlowProviders, resetResolveArgs).ConfigureAwait(false);

            return (SupportsShortcutChildren && LocationType == LocationType.FileSystem && RefreshLinkedChildren()) || changed;
        }

        /// <summary>
        /// Refreshes the linked children.
        /// </summary>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise</returns>
        private bool RefreshLinkedChildren()
        {
            ItemResolveArgs resolveArgs;

            try
            {
                resolveArgs = ResolveArgs;

                if (!resolveArgs.IsDirectory)
                {
                    return false;
                }
            }
            catch (IOException ex)
            {
                Logger.ErrorException("Error getting ResolveArgs for {0}", ex, Path);
                return false;
            }

            var currentManualLinks = LinkedChildren.Where(i => i.Type == LinkedChildType.Manual).ToList();
            var currentShortcutLinks = LinkedChildren.Where(i => i.Type == LinkedChildType.Shortcut).ToList();

            var newShortcutLinks = resolveArgs.FileSystemChildren
                .Where(i => (i.Attributes & FileAttributes.Directory) != FileAttributes.Directory && FileSystem.IsShortcut(i.FullName))
                .Select(i =>
                {
                    try
                    {
                        Logger.Debug("Found shortcut at {0}", i.FullName);

                        var resolvedPath = FileSystem.ResolveShortcut(i.FullName);

                        if (!string.IsNullOrEmpty(resolvedPath))
                        {
                            return new LinkedChild
                            {
                                Path = resolvedPath,
                                Type = LinkedChildType.Shortcut
                            };
                        }

                        Logger.Error("Error resolving shortcut {0}", i.FullName);

                        return null;
                    }
                    catch (IOException ex)
                    {
                        Logger.ErrorException("Error resolving shortcut {0}", ex, i.FullName);
                        return null;
                    }
                })
                .Where(i => i != null)
                .ToList();

            if (!newShortcutLinks.SequenceEqual(currentShortcutLinks, new LinkedChildComparer()))
            {
                Logger.Info("Shortcut links have changed for {0}", Path);

                newShortcutLinks.AddRange(currentManualLinks);
                LinkedChildren = newShortcutLinks;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Folders need to validate and refresh
        /// </summary>
        /// <returns>Task.</returns>
        public override async Task ChangedExternally()
        {
            await base.ChangedExternally().ConfigureAwait(false);

            var progress = new Progress<double>();

            await ValidateChildren(progress, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Marks the played.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="datePlayed">The date played.</param>
        /// <param name="userManager">The user manager.</param>
        /// <returns>Task.</returns>
        public override async Task MarkPlayed(User user, DateTime? datePlayed, IUserDataManager userManager)
        {
            // Sweep through recursively and update status
            var tasks = GetRecursiveChildren(user, true).Where(i => !i.IsFolder && i.LocationType != LocationType.Virtual).Select(c => c.MarkPlayed(user, datePlayed, userManager));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Marks the unplayed.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="userManager">The user manager.</param>
        /// <returns>Task.</returns>
        public override async Task MarkUnplayed(User user, IUserDataManager userManager)
        {
            // Sweep through recursively and update status
            var tasks = GetRecursiveChildren(user, true).Where(i => !i.IsFolder && i.LocationType != LocationType.Virtual).Select(c => c.MarkUnplayed(user, userManager));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds an item by path, recursively
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>BaseItem.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public BaseItem FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException();
            }

            try
            {
                var locationType = LocationType;

                if (locationType == LocationType.Remote && string.Equals(Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return this;
                }

                if (locationType != LocationType.Virtual && ResolveArgs.PhysicalLocations.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    return this;
                }
            }
            catch (IOException ex)
            {
                Logger.ErrorException("Error getting ResolveArgs for {0}", ex, Path);
            }

            return RecursiveChildren.Where(i => i.LocationType != LocationType.Virtual).FirstOrDefault(i =>
            {
                try
                {
                    if (string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (i.LocationType != LocationType.Remote)
                    {
                        if (i.ResolveArgs.PhysicalLocations.Contains(path, StringComparer.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    return false;
                }
                catch (IOException ex)
                {
                    Logger.ErrorException("Error getting ResolveArgs for {0}", ex, Path);
                    return false;
                }
            });
        }
    }
}
