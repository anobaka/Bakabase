import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/api_client.dart';
import '../../core/media_types.dart';
import '../../core/models.dart';
import '../../l10n/app_localizations.dart';
import '../../playback/external_players.dart';
import '../player/player_page.dart';
import '../reader/reader_page.dart';

/// Resource detail: cover, rating, and the resource's files grouped by kind.
///
/// The file list is the ground truth here — `all-files` for directories,
/// `compressed-file/entries` for archives — because `playable-items` only
/// reflects the user's curated profile rules and is legitimately empty on
/// unconfigured libraries. Curated playable items still take precedence for
/// the play section when they exist; everything else falls back to the raw
/// files, so a comic always has all its pages and a video folder is always
/// playable.
class ResourcePage extends StatefulWidget {
  const ResourcePage({super.key, required this.api, required this.resource});

  final BakabaseApiClient api;
  final ResourceSummary resource;

  @override
  State<ResourcePage> createState() => _ResourcePageState();
}

class _ResourcePageState extends State<ResourcePage> {
  List<PlayableItem>? _avItems;
  List<String>? _images;
  List<String>? _otherFiles;
  String? _error;
  double? _rating;

  @override
  void initState() {
    super.initState();
    _load();
    _loadRating();
  }

  Future<List<String>> _listFiles() async {
    final path = widget.resource.path;
    if (widget.resource.isFile) {
      return isCompressedFile(path)
          ? await widget.api.compressedEntries(path)
          : [path];
    }
    return widget.api.allFiles(path);
  }

  Future<void> _load() async {
    try {
      final playableFuture = widget.api.playableItems(widget.resource.id);
      final files = await _listFiles();

      // Archive entries arrive in archive order; all-files is already
      // naturally sorted server-side. Sorting again is cheap and makes page
      // order deterministic either way.
      final images = files.where((f) => classifyPath(f) == MediaKind.image).toList()
        ..sort(naturalCompare);
      final avFiles = files
          .where((f) =>
              classifyPath(f) == MediaKind.video || classifyPath(f) == MediaKind.audio)
          .toList()
        ..sort(naturalCompare);
      final others = files
          .where((f) =>
              classifyPath(f) == MediaKind.other && !isCompressedFile(f))
          .toList()
        ..sort(naturalCompare);

      List<PlayableItem> curated;
      try {
        curated = (await playableFuture)
            .where((i) =>
                classifyPath(i.key) == MediaKind.video ||
                classifyPath(i.key) == MediaKind.audio)
            .toList();
      } on ApiException {
        curated = const [];
      }

      if (!mounted) {
        return;
      }
      setState(() {
        _images = images;
        _avItems = curated.isNotEmpty
            ? curated
            : avFiles.map((f) => PlayableItem(key: f)).toList();
        _otherFiles = others;
      });
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _error = e.message);
      }
    }
  }

  Future<void> _loadRating() async {
    try {
      final rating = await widget.api.resourceRating(widget.resource.id);
      if (mounted) {
        setState(() => _rating = rating);
      }
    } on ApiException {
      // Stars just stay empty; rating is a convenience.
    }
  }

  Future<void> _setRating(double value) async {
    final previous = _rating;
    setState(() => _rating = value);
    try {
      await widget.api.setRating(widget.resource.id, value);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _rating = previous);
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
      }
    }
  }

  void _markPlayed(String itemKey) {
    // Fire and forget, like the web's hand-off: history must never block or
    // delay playback, and nothing after the hand-off is observable anyway.
    widget.api.markPlayed(widget.resource.id, item: itemKey).ignore();
  }

  void _openReader(int initialIndex) {
    final images = _images ?? const [];
    if (images.isEmpty) {
      return;
    }
    _markPlayed(images[initialIndex]);
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => ReaderPage(
          api: widget.api,
          title: widget.resource.title,
          imagePaths: images,
          initialIndex: initialIndex,
        ),
      ),
    );
  }

  void _openInAppPlayer(PlayableItem item) {
    _markPlayed(item.key);
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => PlayerPage(
          url: widget.api.streamUrl(item.key),
          title: item.title,
        ),
      ),
    );
  }

  Future<void> _openExternal(PlayableItem item, ExternalPlayer player) async {
    _markPlayed(item.key);
    final ok = await player.launch(widget.api.streamUrl(item.key));
    if (!ok && mounted) {
      final l10n = AppLocalizations.of(context)!;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(l10n.couldNotOpenPlayer(player.name))),
      );
    }
  }

  Future<void> _copyStreamUrl(String itemKey) async {
    await Clipboard.setData(ClipboardData(text: widget.api.streamUrl(itemKey)));
    _markPlayed(itemKey);
    if (mounted) {
      final l10n = AppLocalizations.of(context)!;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(l10n.streamLinkCopied)),
      );
    }
  }

  void _showPlayOptions(PlayableItem item) {
    final l10n = AppLocalizations.of(context)!;
    final externals = ExternalPlayer.forThisDevice();
    showModalBottomSheet<void>(
      context: context,
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.play_circle),
              title: Text(l10n.playHere),
              subtitle: Text(l10n.builtInPlayer),
              onTap: () {
                Navigator.pop(sheetContext);
                _openInAppPlayer(item);
              },
            ),
            for (final player in externals)
              ListTile(
                leading: const Icon(Icons.open_in_new),
                title: Text(player.id == 'chooser' ? l10n.otherPlayer : player.name),
                onTap: () {
                  Navigator.pop(sheetContext);
                  _openExternal(item, player);
                },
              ),
            ListTile(
              leading: const Icon(Icons.copy),
              title: Text(l10n.copyStreamLink),
              onTap: () {
                Navigator.pop(sheetContext);
                _copyStreamUrl(item.key);
              },
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final resource = widget.resource;
    final coverPath = resource.covers.isNotEmpty ? resource.covers.first : resource.path;
    final loaded = _images != null;
    final images = _images ?? const [];
    final avItems = _avItems ?? const [];
    final others = _otherFiles ?? const [];

    return Scaffold(
      appBar: AppBar(title: Text(resource.title)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Center(
            child: ClipRRect(
              borderRadius: BorderRadius.circular(12),
              child: CachedNetworkImage(
                imageUrl: widget.api.thumbnailUrl(coverPath, width: 640),
                height: 280,
                fit: BoxFit.contain,
                errorWidget: (context, url, error) => const SizedBox(
                  height: 120,
                  child: Icon(Icons.image_not_supported_outlined, size: 48),
                ),
              ),
            ),
          ),
          const SizedBox(height: 12),
          Text(resource.title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 4),
          Text(resource.path, style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 8),
          Row(
            children: [
              for (var star = 1; star <= 5; star++)
                IconButton(
                  visualDensity: VisualDensity.compact,
                  padding: EdgeInsets.zero,
                  icon: Icon(
                    (_rating ?? 0) >= star
                        ? Icons.star
                        : (_rating ?? 0) >= star - 0.5
                            ? Icons.star_half
                            : Icons.star_border,
                    color: Colors.amber,
                  ),
                  onPressed: () => _setRating(star.toDouble()),
                ),
              if (_rating != null)
                Text(_rating!.toStringAsFixed(1),
                    style: Theme.of(context).textTheme.bodySmall),
            ],
          ),
          const SizedBox(height: 8),
          if (_error != null)
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error))
          else if (!loaded)
            const Center(
              child: Padding(
                padding: EdgeInsets.all(16),
                child: CircularProgressIndicator(),
              ),
            )
          else ...[
            if (images.isNotEmpty) ...[
              FilledButton.icon(
                icon: const Icon(Icons.auto_stories),
                label: Text(l10n.readPages(images.length)),
                onPressed: () => _openReader(0),
              ),
              const SizedBox(height: 12),
            ],
            if (avItems.isNotEmpty) ...[
              Text(l10n.playSection, style: Theme.of(context).textTheme.titleSmall),
              const SizedBox(height: 4),
              for (final item in avItems)
                Card(
                  child: ListTile(
                    leading: Icon(classifyPath(item.key) == MediaKind.audio
                        ? Icons.music_note_outlined
                        : Icons.play_circle_outline),
                    title: Text(item.title),
                    onTap: () => _showPlayOptions(item),
                  ),
                ),
            ],
            if (others.isNotEmpty) ...[
              const SizedBox(height: 8),
              Text(l10n.otherFiles, style: Theme.of(context).textTheme.titleSmall),
              const SizedBox(height: 4),
              for (final file in others)
                Card(
                  child: ListTile(
                    leading: const Icon(Icons.insert_drive_file_outlined),
                    title: Text(PlayableItem(key: file).title),
                    trailing: const Icon(Icons.copy, size: 18),
                    onTap: () => _copyStreamUrl(file),
                  ),
                ),
            ],
            if (images.isEmpty && avItems.isEmpty && others.isEmpty)
              Text(l10n.noPlayableFiles),
          ],
        ],
      ),
    );
  }
}
