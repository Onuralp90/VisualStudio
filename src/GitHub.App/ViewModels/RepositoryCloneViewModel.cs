﻿using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using GitHub.Caches;
using GitHub.Exports;
using GitHub.Extensions.Reactive;
using GitHub.Models;
using GitHub.Services;
using NullGuard;
using Octokit;
using ReactiveUI;

namespace GitHub.ViewModels
{
    [ExportViewModel(ViewType=UIViewType.Clone)]
    public class RepositoryCloneViewModel : BaseViewModel, IRepositoryCloneViewModel
    {
        readonly IRepositoryCloneService cloneService;

        [ImportingConstructor]
        RepositoryCloneViewModel(
            IConnectionRepositoryHostMap connectionRepositoryHostMap,
            IRepositoryCloneService repositoryCloneService,
            IAvatarProvider avatarProvider)
            : this(connectionRepositoryHostMap.CurrentRepositoryHost, repositoryCloneService, avatarProvider)
        { }
        
        public RepositoryCloneViewModel(
            IRepositoryHost repositoryHost,
            IRepositoryCloneService cloneService,
            IAvatarProvider avatarProvider)
        {
            this.cloneService = cloneService;
            Title = string.Format(CultureInfo.CurrentCulture, "Clone a {0} Repository", repositoryHost.Title);
            // TODO: How do I know which host this dialog is associated with?
            // For now, I'll assume GitHub Host.
            Repositories = new ReactiveList<IRepositoryModel>();
            repositoryHost.ApiClient.GetUserRepositories()
                .FirstAsync()
                .Flatten()
                .Select(repo => CreateRepository(repo, avatarProvider))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(Repositories.Add);

            filterTextIsEnabled = this.WhenAny(x => x.Repositories.Count, x => x.Value > 0)
                .ToProperty(this, x => x.FilterTextIsEnabled);

            var filterResetSignal = this.WhenAny(x => x.FilterText, x => x.Value)
                .DistinctUntilChanged(StringComparer.OrdinalIgnoreCase)
                .Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler);

            FilteredRepositories = Repositories.CreateDerivedCollection(
                x => x,
                filter: FilterRepository,
                signalReset: filterResetSignal
            );

            CloneCommand = ReactiveCommand.CreateAsyncObservable(OnCloneRepository);

            BaseRepositoryPath = cloneService.GetLocalClonePathFromGitProvider(cloneService.DefaultClonePath);
        }

        static IRepositoryModel CreateRepository(Repository repository, IAvatarProvider avatarProvider)
        {
            var owner = new CachedAccount(repository.Owner);
            return new RepositoryModel(repository, new Models.Account(owner, avatarProvider.GetAvatar(owner)));
        }

        bool FilterRepository(IRepositoryModel repo)
        {
            if (string.IsNullOrWhiteSpace(FilterText))
                return true;

            // Not matching on NameWithOwner here since that's already been filtered on by the selected account
            return repo.Name.IndexOf(FilterText ?? "", StringComparison.OrdinalIgnoreCase) != -1;
        }

        IObservable<Unit> OnCloneRepository(object state)
        {
            return Observable.Start(() =>
            {
                var repository = SelectedRepository;
                if (!Directory.Exists(BaseRepositoryPath))
                    Directory.CreateDirectory(BaseRepositoryPath);
                return cloneService.CloneRepository(repository.CloneUrl, repository.Name, BaseRepositoryPath);
            })
            .SelectMany(_ => _);
        }

        string baseRepositoryPath;
        /// <summary>
        /// Path to clone repositories into
        /// </summary>
        public string BaseRepositoryPath
        {
            [return: AllowNull]
            get { return baseRepositoryPath; }
            set { this.RaiseAndSetIfChanged(ref baseRepositoryPath, value); }
        }

        /// <summary>
        /// Fires off the cloning process
        /// </summary>
        public IReactiveCommand<Unit> CloneCommand { get; private set; }

        IReactiveList<IRepositoryModel> repositories;
        /// <summary>
        /// List of repositories as returned by the server
        /// </summary>
        public IReactiveList<IRepositoryModel> Repositories
        {
            get { return repositories; }
            private set { this.RaiseAndSetIfChanged(ref repositories, value); }
        }

        IReactiveDerivedList<IRepositoryModel> filteredRepositories;
        /// <summary>
        /// List of repositories as filtered by user
        /// </summary>
        public IReactiveDerivedList<IRepositoryModel> FilteredRepositories
        {
            get { return filteredRepositories; }
            private set { this.RaiseAndSetIfChanged(ref filteredRepositories, value); }
        }

        IRepositoryModel selectedRepository;
        /// <summary>
        /// Selected repository to clone
        /// </summary>
        [AllowNull]
        public IRepositoryModel SelectedRepository
        {
            [return: AllowNull]
            get { return selectedRepository; }
            set { this.RaiseAndSetIfChanged(ref selectedRepository, value); }
        }

        readonly ObservableAsPropertyHelper<bool> filterTextIsEnabled;
        /// <summary>
        /// True if there are repositories (otherwise no point in filtering)
        /// </summary>
        public bool FilterTextIsEnabled { get { return filterTextIsEnabled.Value; } }

        string filterText;
        /// <summary>
        /// User text to filter the repositories list
        /// </summary>
        [AllowNull]
        public string FilterText
        {
            [return: AllowNull]
            get { return filterText; }
            set { this.RaiseAndSetIfChanged(ref filterText, value); }
        }
    }
}