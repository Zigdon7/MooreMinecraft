# Atlas client accessibility review

This source review is **not in-game rendering or narrator proof**, and does not
assign a visual quality score. No server behavior or protocol was changed.

## Covered by this change

- Full map terrain is scissored to its viewport, like the HUD.
- Player cross, lodestone square, and marker diamond use dark backplates and
  distinct shapes; the map includes a textual legend and north indicator.
- Rebound open keys appear in screen/HUD hints; narrow labels are width-bounded.
- Destination tooltips and narration retain full names and coordinates even when
  the visible label is shortened. Selection has a textual prefix.
- Screen narration explains keyboard navigation, map status, lodestone count,
  balance, and travel prerequisites. Async refresh restores matching focus.
- Travel hints distinguish missing selection, pending refresh, missing quote,
  expired quote, insufficient balance, and ready-to-travel state.
- Compact mode retains paginated destination selection, quote, and travel rather
  than removing these operations. Layout coverage includes 240x150, 320x150, and 320x180.
- The Text button opens a paginated textual/narrated equivalent for every
  lodestone and marker (full name, direction, distance, coordinates), plus current
  position/heading. It includes Refresh and works without the minimap.
- The HUD button provides visibility, map/text mode, three sizes, and four
  corners. Preferences persist atomically in the game config directory as
  `saturn-atlas.properties`; failed saves explicitly remain session-only.
- Controls provides optional unbound Toggle Atlas HUD and Describe Atlas map
  shortcuts, avoiding conflicts with existing game bindings.
- HUD/text/screen status distinguishes current, updating, error, timed-out, and
  aged snapshots (15 seconds). Cached data is retained and explicitly marked.
- Pagination is labelled Prev/Next with a page count.

## Required interactive verification before a quality rating

1. Open in Minecraft Java 26.3 with Fabric, at GUI sizes 380x200, 400x220,
   640x360, and compact 320x180. Check terrain edges, notices, legend, and footer
   for clipping/overlap, including highest GUI Scale and a resize while open.
2. Rebind M, confirm HUD/header reflect the new binding, and close using it/Esc.
3. Use Tab/Shift Tab and Enter without a mouse. Select a long-named destination;
   wait for periodic sync; ensure focus remains useful and full tooltip is readable.
4. Enable narrator and verify full destination names, selection state, keyboard
   instructions, status, balance, and travel prerequisites are spoken sensibly.
   Open Text; traverse all markers/lodestones with keyboard only and compare
   heading/cardinal directions, distance, and coordinates with the world.
   Test compact 320x180 selection, quote, and travel prerequisites.
   Include compact 320x150; verify the inline destination pager, overview page,
   point navigation boundaries, and arrival/error narration after Text Refresh.
5. Open HUD; toggle visibility and map/text mode, all sizes and all corners;
   restart the client and verify persistence. Bind both optional shortcuts in
   Controls. Stop replies for over 15 seconds and verify stale text appears.
6. Populate multiple pages; check Prev/Next/page count and final partial page.
7. Check empty terrain, zero anchors, waiting, timeout, malformed/server-error
   reply, insufficient funds, and quote expiration. Disabled Travel must never
   issue a command. Verify color-independent symbols against varied terrain.
8. Actual travel, server payment, permissions, and Bedrock behavior require
   separate authorized integration testing; headless client tests do not prove them.
