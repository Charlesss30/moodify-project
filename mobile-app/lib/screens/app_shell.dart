import 'package:flutter/material.dart';

import 'welcome_tab.dart';
import '../models/session_user.dart';
import 'history_screen.dart';
import 'library_screen.dart';
import 'mood_discovery_screen.dart';
import 'profile_screen.dart';

class AppShell extends StatefulWidget {
  const AppShell({
    super.key,
    required this.currentUser,
    required this.onLogoutConfirmed,
  });

  final SessionUser currentUser;
  final VoidCallback onLogoutConfirmed;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  int _activeTab = 0;

  @override
  Widget build(BuildContext context) {
    final pages = [
      WelcomeTab(
        user: widget.currentUser,
        onDiscover: () => setState(() => _activeTab = 1),
      ),
      const MoodDiscoveryScreen(),
      const LibraryScreen(),
      const HistoryScreen(),
      ProfileScreen(
        user: widget.currentUser,
        onLogoutConfirmed: widget.onLogoutConfirmed,
      ),
    ];
    return Scaffold(
      body: SafeArea(
        child: IndexedStack(index: _activeTab, children: pages),
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _activeTab,
        onDestinationSelected: (index) => setState(() => _activeTab = index),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home),
            label: 'Trang chủ',
          ),
          NavigationDestination(
            icon: Icon(Icons.explore_outlined),
            selectedIcon: Icon(Icons.explore),
            label: 'Khám phá',
          ),
          NavigationDestination(
            icon: Icon(Icons.video_library_outlined),
            selectedIcon: Icon(Icons.video_library),
            label: 'Thư viện',
          ),
          NavigationDestination(icon: Icon(Icons.history), label: 'Lịch sử'),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Cá nhân',
          ),
        ],
      ),
    );
  }
}
