export function isSidebarExpanded(
  collapsed: boolean,
  hovered: boolean,
  profileMenuOpen: boolean,
) {
  return !collapsed || hovered || profileMenuOpen;
}

export function getProfileMenuSide(collapsed: boolean) {
  return collapsed ? "right" : "top";
}
