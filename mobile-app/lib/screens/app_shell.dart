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
  int _savedRequest = 0;
  final Set<int> _visitedTabs = {0};
  void _selectTab(int index) {
    Tooltip.dismissAllToolTips();
    FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      _visitedTabs.add(index);
      _activeTab = index;
    });
  }

  late SessionUser _currentUser;
  @override
  void initState() {
    super.initState();
    _currentUser = widget.currentUser;
  }

  @override
  Widget build(BuildContext context) {
    final pages = [
      WelcomeTab(user: _currentUser, onDiscover: () => _selectTab(1)),
      const MoodDiscoveryScreen(),
      LibraryScreen(savedRequest: _savedRequest, active: _activeTab == 2),
      const HistoryScreen(),
      ProfileScreen(
        user: _currentUser,
        active: _activeTab == 4,
        onOpenSaved: () {
          _savedRequest++;
          _selectTab(2);
        },
        onUserUpdated: (user) => setState(() => _currentUser = user),
        onLogoutConfirmed: widget.onLogoutConfirmed,
      ),
    ];
    return Scaffold(
      body: SafeArea(
        child: IndexedStack(
          index: _activeTab,
          children: [
            for (var i = 0; i < pages.length; i++)
              TickerMode(
                enabled: i == _activeTab,
                child: _visitedTabs.contains(i)
                    ? pages[i]
                    : const SizedBox.shrink(),
              ),
          ],
        ),
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _activeTab,
        animationDuration: MediaQuery.disableAnimationsOf(context)
            ? Duration.zero
            : const Duration(milliseconds: 220),
        onDestinationSelected: _selectTab,
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
