import type * as MotionModule from "framer-motion";

import { createRequire } from "node:module";

import { animate } from "framer-motion";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const require = createRequire(import.meta.url);
const commonJsMotion = require("framer-motion") as typeof MotionModule;

// Exercise the installed dependency, including its patch-package backport. A
// jsdom animation stub exposes the handoff instant that visual tests can miss.
// Upstream: motiondivision/motion@84ec93335f6e7e1040f1870d6254f2dd77241cd4.
describe.each([
  ["ES modules", animate],
  ["CommonJS", commonJsMotion.animate],
] as const)("WAAPI completion (%s)", (_moduleFormat, animateElement) => {
  const originalAnimate = Object.getOwnPropertyDescriptor(Element.prototype, "animate");
  let nativeAnimations: {
    element: HTMLElement;
    onfinish: (() => void) | null;
    cancel: ReturnType<typeof vi.fn>;
    inlineStyleAtCancel?: string;
  }[];

  beforeEach(() => {
    nativeAnimations = [];
    Object.defineProperty(Element.prototype, "animate", {
      configurable: true,
      value: function (this: HTMLElement) {
        const element = this;
        const animation = {
          element,
          onfinish: null as (() => void) | null,
          startTime: 0,
          currentTime: 300,
          playbackRate: 1,
          playState: "running",
          inlineStyleAtCancel: undefined as string | undefined,
          cancel: vi.fn(() => {
            animation.inlineStyleAtCancel = element.style.cssText;
          }),
          play: vi.fn(),
          pause: vi.fn(),
        };

        nativeAnimations.push(animation);

        return animation;
      },
    });
  });

  afterEach(() => {
    if (originalAnimate) {
      Object.defineProperty(Element.prototype, "animate", originalAnimate);
    } else {
      Reflect.deleteProperty(Element.prototype, "animate");
    }
    document.body.replaceChildren();
  });

  it.each([
    { property: "opacity", initial: 0, target: 1 },
    { property: "transform", initial: "scale(0.8)", target: "scale(1)" },
    { property: "clipPath", initial: "inset(10%)", target: "inset(0%)" },
    { property: "filter", initial: "blur(4px)", target: "blur(0px)" },
  ] as const)(
    "commits $property before removing the native animation",
    ({ property, initial, target }) => {
      const element = document.createElement("div");

      element.style[property] = String(initial);
      document.body.append(element);
      const onComplete = vi.fn(() => {
        expect(element.style[property]).toBe(String(target));
      });
      const controls = animateElement(
        element,
        { [property]: [initial, target] },
        { duration: 0.3, onComplete },
      );

      // Reading duration flushes keyframe resolution without advancing a render.
      expect(controls.duration).toBe(0.3);
      const native = nativeAnimations.find((animation) => animation.element === element);

      expect(native?.onfinish).toBeTypeOf("function");
      native!.onfinish!();

      const committedStyle = document.createElement("div").style;

      committedStyle.cssText = native!.inlineStyleAtCancel!;
      expect(committedStyle[property]).toBe(String(target));
      expect(element.style[property]).toBe(String(target));
      expect(native!.cancel).toHaveBeenCalledOnce();
      expect(onComplete).toHaveBeenCalledOnce();
    },
  );

  it("commits the initial keyframe when a reverse repeat ends there", () => {
    const element = document.createElement("div");

    element.style.opacity = "0.25";
    document.body.append(element);
    const controls = animateElement(
      element,
      { opacity: [0, 1] },
      {
        duration: 0.3,
        repeat: 1,
        repeatType: "reverse",
      },
    );

    expect(controls.duration).toBe(0.3);
    const native = nativeAnimations.find((animation) => animation.element === element);

    expect(native?.onfinish).toBeTypeOf("function");
    native!.onfinish!();
    expect(native!.inlineStyleAtCancel).toBe("opacity: 0;");
  });
});
