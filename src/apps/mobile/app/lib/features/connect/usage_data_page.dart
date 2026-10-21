import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/analytics.dart';
import '../../core/connection.dart';
import '../../l10n/app_localizations.dart';

class UsageDataPage extends ConsumerStatefulWidget {
  const UsageDataPage({super.key});

  @override
  ConsumerState<UsageDataPage> createState() => _UsageDataPageState();
}

class _UsageDataPageState extends ConsumerState<UsageDataPage> {
  bool _saving = false;

  Future<void> _change(bool enabled) async {
    setState(() => _saving = true);
    await ref.read(mobileAnalyticsProvider).setLocalEnabled(enabled);
    await ref
        .read(connectionProvider.notifier)
        .refreshAnalytics(newSession: true);
    if (mounted) setState(() => _saving = false);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final analytics = ref.watch(mobileAnalyticsProvider);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.usageDataTitle)),
      body: ListView(
        children: [
          SwitchListTile(
            title: Text(l10n.usageDataTitle),
            value: analytics.localEnabled,
            onChanged: _saving ? null : _change,
          ),
          Padding(
            padding: const EdgeInsets.all(16),
            child: Text(l10n.usageDataDescription),
          ),
        ],
      ),
    );
  }
}
