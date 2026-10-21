import 'dart:async';

import 'package:flutter/material.dart';
import 'package:media_kit/media_kit.dart';
import 'package:media_kit_video/media_kit_video.dart';

/// In-app playback via libmpv: the same `/file/raw` bytes a native player
/// would pull, decoded on this device — MKV/HEVC/DTS included, with working
/// seek and zero server CPU. Also handles audio (video area stays black).
class PlayerPage extends StatefulWidget {
  const PlayerPage({
    super.key,
    required this.url,
    required this.title,
    this.onStarted,
  });

  final String url;
  final String title;
  final VoidCallback? onStarted;

  @override
  State<PlayerPage> createState() => _PlayerPageState();
}

class _PlayerPageState extends State<PlayerPage> {
  late final Player _player = Player();
  late final VideoController _controller = VideoController(_player);
  StreamSubscription<Duration>? _positionSubscription;
  bool _started = false;

  @override
  void initState() {
    super.initState();
    _positionSubscription = _player.stream.position.listen((position) {
      // Opening/accepting a URL is not playback. Wait for actual progress.
      if (!_started && position > Duration.zero && _player.state.playing) {
        _started = true;
        widget.onStarted?.call();
      }
    });
    _player.open(Media(widget.url));
  }

  @override
  void dispose() {
    _positionSubscription?.cancel();
    _player.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text(widget.title, maxLines: 1, overflow: TextOverflow.ellipsis),
      ),
      body: Center(child: Video(controller: _controller)),
    );
  }
}
