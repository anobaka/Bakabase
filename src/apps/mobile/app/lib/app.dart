import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/connection.dart';
import 'features/connect/connect_page.dart';
import 'features/library/library_page.dart';

class BakabaseApp extends ConsumerWidget {
  const BakabaseApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final connection = ref.watch(connectionProvider);

    return MaterialApp(
      title: 'Bakabase',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF0E7C6B)),
      ),
      darkTheme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF0E7C6B),
          brightness: Brightness.dark,
        ),
      ),
      // The app cannot exist without a server: everything except the connect
      // flow lives behind a successful handshake.
      home: switch (connection) {
        Connected() => const LibraryPage(),
        _ => const ConnectPage(),
      },
    );
  }
}
