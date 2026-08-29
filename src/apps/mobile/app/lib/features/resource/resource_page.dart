import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/api_client.dart';
import '../../core/media_types.dart';
import '../../core/models.dart';
import '../../playback/external_players.dart';
import '../player/player_page.dart';
import '../reader/reader_page.dart';

/// Resource detail: cover, playable files grouped by kind. Images open the
/// reader; video/audio offer the in-app player, an external player, or the
/// copyable stream link — every route records play history.
class ResourcePage extends StatefulWidget {
  const ResourcePage({super.key, required this.api, required this.resource});

  final BakabaseApiClient api;
  final ResourceSummary resource;

  @override
  State<ResourcePage> createState() => _ResourcePageState();
}

class _ResourcePageState extends State<ResourcePage> {
  List<PlayableItem>? _items;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final items = await widget.api.playableItems(widget.resource.id);
      if (mounted) {
        setState(() => _items = items);
      }
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _error = e.message);
      }
    }
  }

  List<PlayableItem> get _images =>
      (_items ?? const []).where((i) => classifyPath(i.key) == MediaKind.image).toList();

  List<PlayableItem> get _avItems => (_items ?? const [])
      .where((i) =>
          classifyPath(i.key) == MediaKind.video || classifyPath(i.key) == MediaKind.audio)
      .toList();

  List<PlayableItem> get _otherItems =>
      (_items ?? const []).where((i) => classifyPath(i.key) == MediaKind.other).toList();

  void _markPlayed(String itemKey) {
    // Fire and forget, like the web's hand-off: history must never block or
    // delay playback, and nothing after the hand-off is observable anyway.
    widget.api.markPlayed(widget.resource.id, item: itemKey).ignore();
  }

  void _openReader(int initialIndex) {
    final images = _images;
    if (images.isEmpty) {
      return;
    }
    _markPlayed(images[initialIndex].key);
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => ReaderPage(
          api: widget.api,
          title: widget.resource.title,
          imagePaths: images.map((i) => i.key).toList(),
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
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Could not open ${player.name} — is it installed?')),
      );
    }
  }

  Future<void> _copyStreamUrl(PlayableItem item) async {
    await Clipboard.setData(ClipboardData(text: widget.api.streamUrl(item.key)));
    _markPlayed(item.key);
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Stream link copied — paste it into a player')),
      );
    }
  }

  void _showPlayOptions(PlayableItem item) {
    final externals = ExternalPlayer.forThisDevice();
    showModalBottomSheet<void>(
      context: context,
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.play_circle),
              title: const Text('Play here'),
              subtitle: const Text('Built-in player (mpv)'),
              onTap: () {
                Navigator.pop(sheetContext);
                _openInAppPlayer(item);
              },
            ),
            for (final player in externals)
              ListTile(
                leading: const Icon(Icons.open_in_new),
                title: Text(player.name),
                onTap: () {
                  Navigator.pop(sheetContext);
                  _openExternal(item, player);
                },
              ),
            ListTile(
              leading: const Icon(Icons.copy),
              title: const Text('Copy stream link'),
              onTap: () {
                Navigator.pop(sheetContext);
                _copyStreamUrl(item);
              },
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final resource = widget.resource;
    final coverPath = resource.covers.isNotEmpty ? resource.covers.first : resource.path;
    final images = _images;
    final avItems = _avItems;
    final others = _otherItems;

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
          const SizedBox(height: 16),
          if (_error != null)
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error))
          else if (_items == null)
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
                label: Text('Read (${images.length} pages)'),
                onPressed: () => _openReader(0),
              ),
              const SizedBox(height: 12),
            ],
            if (avItems.isNotEmpty) ...[
              Text('Play', style: Theme.of(context).textTheme.titleSmall),
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
              Text('Other files', style: Theme.of(context).textTheme.titleSmall),
              const SizedBox(height: 4),
              for (final item in others)
                Card(
                  child: ListTile(
                    leading: const Icon(Icons.insert_drive_file_outlined),
                    title: Text(item.title),
                    trailing: const Icon(Icons.copy, size: 18),
                    onTap: () => _copyStreamUrl(item),
                  ),
                ),
            ],
            if (_items!.isEmpty)
              const Text('No playable files were found in this resource.'),
          ],
        ],
      ),
    );
  }
}
