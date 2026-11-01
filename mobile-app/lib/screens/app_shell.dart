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
  // Keep page identities stable while presenting Discovery as the central destination.
  static const _destinationTabs = [0, 2, 1, 3, 4];
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
        selectedIndex: _destinationTabs.indexOf(_activeTab),
        height: 78,
        indicatorColor: Colors.transparent,
        animationDuration: MediaQuery.disableAnimationsOf(context)
            ? Duration.zero
            : const Duration(milliseconds: 220),
        onDestinationSelected: (index) => _selectTab(_destinationTabs[index]),
        destinations: [
          const NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home),
            label: 'Trang chủ',
          ),
          const NavigationDestination(
            icon: Icon(Icons.video_library_outlined),
            selectedIcon: Icon(Icons.video_library),
            label: 'Thư viện',
          ),
          NavigationDestination(
            icon: _DiscoveryNavigationIcon(selected: _activeTab == 1),
            label: 'Khám phá',
          ),
          const NavigationDestination(
            icon: Icon(Icons.history),
            label: 'Lịch sử',
          ),
          const NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Cá nhân',
          ),
        ],
      ),
    );
  }
}

class _DiscoveryNavigationIcon extends StatelessWidget {
  const _DiscoveryNavigationIcon({required this.selected});
  final bool selected;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return AnimatedContainer(
      key: const ValueKey('primary-discovery-tab-icon'),
      duration: MediaQuery.disableAnimationsOf(context)
          ? Duration.zero
          : const Duration(milliseconds: 180),
      curve: Curves.easeOutCubic,
      width: 54,
      height: 44,
      decoration: BoxDecoration(
        color: selected
            ? scheme.primary
            : scheme.primary.withValues(alpha: .16),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(
          color: scheme.primary.withValues(alpha: selected ? 1 : .38),
        ),
      ),
      child: Icon(
        selected ? Icons.explore_rounded : Icons.explore_outlined,
        size: 30,
        color: selected ? scheme.onPrimary : scheme.primary,
      ),
    );
  }
}
