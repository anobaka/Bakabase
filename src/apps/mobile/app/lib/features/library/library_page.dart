import 'dart:async';

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api_client.dart';
import '../../core/connection.dart';
import '../../core/models.dart';
import '../history/history_page.dart';
import '../resource/resource_page.dart';
import '../../l10n/app_localizations.dart';

/// The sort choices offered in the grid; values mirror the server's
/// ResourceSearchSortableProperty. Labels live in the l10n layer.
enum SortChoice {
  addDt(6),
  playedAt(11),
  fileModifyDt(2),
  filename(3);

  const SortChoice(this.property);

  final int property;

  String label(AppLocalizations l10n) => switch (this) {
        SortChoice.addDt => l10n.sortAddDt,
        SortChoice.playedAt => l10n.sortPlayedAt,
        SortChoice.fileModifyDt => l10n.sortFileModifyDt,
        SortChoice.filename => l10n.sortFilename,
      };
}

/// The main browsing surface: media-library chips, keyword search, and an
/// infinite-scrolling resource grid. All state is server-side; this page only
/// pages through search results.
class LibraryPage extends ConsumerStatefulWidget {
  const LibraryPage({super.key});

  @override
  ConsumerState<LibraryPage> createState() => _LibraryPageState();
}

class _LibraryPageState extends ConsumerState<LibraryPage> {
  static const _pageSize = 60;

  final ScrollController _scroll = ScrollController();
  final TextEditingController _keyword = TextEditingController();
  Timer? _debounce;

  List<MediaLibrary> _libraries = const [];
  int? _selectedLibraryId;
  SortChoice _sort = SortChoice.addDt;
  bool _sortAsc = false;

  final List<ResourceSummary> _resources = [];
  int _page = 0;
  int _totalCount = 0;
  bool _loading = false;
  String? _error;

  BakabaseApiClient get _api => (ref.read(connectionProvider) as Connected).api;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_maybeLoadMore);
    _reload();
    _loadLibraries();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _scroll.dispose();
    _keyword.dispose();
    super.dispose();
  }

  Future<void> _loadLibraries() async {
    try {
      final libraries = await _api.mediaLibraries();
      if (mounted) {
        setState(() => _libraries = libraries);
      }
    } on ApiException {
      // Chips are a convenience; browsing works without them.
    }
  }

  Future<void> _reload() async {
    setState(() {
      _resources.clear();
      _page = 0;
      _totalCount = 0;
      _error = null;
    });
    await _loadNextPage();
  }

  void _maybeLoadMore() {
    if (_scroll.position.extentAfter < 600) {
      _loadNextPage();
    }
  }

  Future<void> _loadNextPage() async {
    if (_loading || (_page > 0 && _resources.length >= _totalCount)) {
      return;
    }
    _loading = true;

    try {
      final result = await _api.search(
        keyword: _keyword.text,
        mediaLibraryId: _selectedLibraryId,
        page: _page + 1,
        pageSize: _pageSize,
        sortProperty: _sort.property,
        sortAsc: _sortAsc,
      );
      if (!mounted) {
        return;
      }
      setState(() {
        _page = result.page;
        _totalCount = result.totalCount;
        _resources.addAll(result.resources);
      });
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _error = e.message);
      }
    } finally {
      _loading = false;
    }
  }

  void _onKeywordChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), _reload);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final connection = ref.watch(connectionProvider);
    if (connection is! Connected) {
      return const SizedBox.shrink();
    }

    return Scaffold(
      appBar: AppBar(
        title: Text(connection.server.name),
        actions: [
          PopupMenuButton<String>(
            tooltip: l10n.sortTooltip,
            icon: const Icon(Icons.sort),
            onSelected: (value) {
              setState(() {
                if (value == 'direction') {
                  _sortAsc = !_sortAsc;
                } else {
                  _sort = SortChoice.values.byName(value);
                }
              });
              _reload();
            },
            itemBuilder: (context) => [
              for (final choice in SortChoice.values)
                CheckedPopupMenuItem(
                  value: choice.name,
                  checked: _sort == choice,
                  child: Text(choice.label(l10n)),
                ),
              const PopupMenuDivider(),
              PopupMenuItem(
                value: 'direction',
                child: Text(_sortAsc ? l10n.ascending : l10n.descending),
              ),
            ],
          ),
          IconButton(
            tooltip: l10n.playHistoryTooltip,
            icon: const Icon(Icons.history),
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(builder: (_) => HistoryPage(api: _api)),
            ),
          ),
          IconButton(
            // The escape hatch to everything the thin app deliberately does
            // not implement: the full desktop web UI, in the system browser.
            tooltip: l10n.openWebUiTooltip,
            icon: const Icon(Icons.open_in_browser),
            onPressed: () => launchUrl(
              Uri.parse(_api.baseUrl),
              mode: LaunchMode.externalApplication,
            ),
          ),
          IconButton(
            tooltip: l10n.switchServerTooltip,
            icon: const Icon(Icons.swap_horiz),
            onPressed: () => ref.read(connectionProvider.notifier).disconnect(),
          ),
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 4),
            child: TextField(
              controller: _keyword,
              decoration: InputDecoration(
                prefixIcon: const Icon(Icons.search),
                hintText: l10n.searchHint,
                border: const OutlineInputBorder(),
                isDense: true,
              ),
              onChanged: _onKeywordChanged,
            ),
          ),
          if (_libraries.isNotEmpty)
            SizedBox(
              height: 44,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 12),
                children: [
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: FilterChip(
                      label: Text(l10n.allLibraries),
                      selected: _selectedLibraryId == null,
                      onSelected: (_) {
                        setState(() => _selectedLibraryId = null);
                        _reload();
                      },
                    ),
                  ),
                  for (final library in _libraries)
                    Padding(
                      padding: const EdgeInsets.only(right: 8),
                      child: FilterChip(
                        label: Text('${library.name} (${library.resourceCount})'),
                        selected: _selectedLibraryId == library.id,
                        onSelected: (_) {
                          setState(() => _selectedLibraryId = library.id);
                          _reload();
                        },
                      ),
                    ),
                ],
              ),
            ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Text(
                _error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: _reload,
              child: GridView.builder(
                controller: _scroll,
                padding: const EdgeInsets.all(12),
                gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                  maxCrossAxisExtent: 160,
                  mainAxisSpacing: 8,
                  crossAxisSpacing: 8,
                  childAspectRatio: 0.7,
                ),
                itemCount: _resources.length,
                itemBuilder: (context, index) {
                  if (index == _resources.length - 1) {
                    // Reaching the last built cell is the load-more signal that
                    // also works before the grid overflows the viewport.
                    WidgetsBinding.instance.addPostFrameCallback((_) => _maybeLoadMore());
                  }
                  return _ResourceCell(api: _api, resource: _resources[index]);
                },
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ResourceCell extends StatelessWidget {
  const _ResourceCell({required this.api, required this.resource});

  final BakabaseApiClient api;
  final ResourceSummary resource;

  @override
  Widget build(BuildContext context) {
    final coverPath = resource.covers.isNotEmpty ? resource.covers.first : resource.path;

    return InkWell(
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute<void>(
          builder: (_) => ResourcePage(api: api, resource: resource),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(
            child: ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: CachedNetworkImage(
                imageUrl: api.thumbnailUrl(coverPath, width: 320),
                fit: BoxFit.cover,
                errorWidget: (context, url, error) => Container(
                  color: Theme.of(context).colorScheme.surfaceContainerHighest,
                  child: const Icon(Icons.folder_outlined, size: 40),
                ),
              ),
            ),
          ),
          const SizedBox(height: 4),
          Text(
            resource.title,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}
