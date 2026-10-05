# Dependency patches

## Framer Motion 11.18.2: native animation completion

`framer-motion+11.18.2.patch` backports the final-style commit from upstream
[commit 84ec933](https://github.com/motiondivision/motion/commit/84ec93335f6e7e1040f1870d6254f2dd77241cd4),
released in Motion 12.34.5. Version 11.18.2 is the last published v11 release.

The v11 completion handler updates its MotionValue, which schedules a later DOM
render, then immediately cancels the native animation. Removing the animation's
fill can expose the old inline opacity and transform for one frame. This caused
HeroUI popovers and other animated overlays to disappear briefly at the end of
their entrance animation.

The patch writes the resolved final value to the original animated element before
completion callbacks and cancellation. It preserves native animations and handles
all v11 accelerated CSS properties (`opacity`, `transform`, `clipPath`, `filter`),
including reversed repeats. Both the ESM implementation and the CommonJS bundles
that include it are patched. The existing `postinstall` runs `patch-package`.

The regression test at `src/test/regressions/framerMotionWaapi.test.ts` checks the
inline style at the instant the native animation is canceled, before any scheduled
DOM render can hide the regression. It covers both module formats.

Remove this patch when upgrading Framer Motion to a release with the upstream fix,
then rerun these tests and check HeroUI tooltip, popover, dropdown, and modal
entrance/exit animations in the browser. Do not carry the patch forward blindly.
