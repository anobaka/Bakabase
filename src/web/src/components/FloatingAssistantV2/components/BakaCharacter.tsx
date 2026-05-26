import type { BakaState, IdlePose, HoverPose, ClickPose, PeekPose, DockedEdge } from "../types";

import React, { useMemo } from "react";

import { BAKA_SIZE } from "../constants";

interface Props {
  state: BakaState;
  idlePose: IdlePose;
  hoverPose: HoverPose;
  clickPose: ClickPose;
  peekPose: PeekPose;
  dockedEdge: DockedEdge;
  isWorking?: boolean;
}

/**
 * A cute round character (小baka) rendered entirely in SVG + CSS animations.
 * Body is a round blob, with expressive eyes, a small mouth, tiny arms, and blush marks.
 */
const BakaCharacter: React.FC<Props> = ({
  state,
  idlePose,
  hoverPose,
  clickPose,
  peekPose,
  dockedEdge,
  isWorking = false,
}) => {
  // Working overlay only appears in passive states; user interactions take priority.
  const showWorking = isWorking && (state === "docked" || state === "floating-idle");

  const animClass = useMemo(() => {
    switch (state) {
      case "floating-idle":
        return `idle-${idlePose}`;
      case "hovered":
        return `hover-${hoverPose}`;
      case "clicked":
        return `click-${clickPose}`;
      case "peeking":
        return `peek-${peekPose}`;
      case "dragging":
        return "dragging";
      case "docked":
        return "docked";
      case "chatting":
        return "chatting";
      case "waving-bye":
        return "waving-bye";
      default:
        return "idle-breathe";
    }
  }, [state, idlePose, hoverPose, clickPose, peekPose]);

  // Eyes change based on state
  const eyes = useMemo(() => {
    if (state === "clicked" && clickPose === "heart") {
      // Heart eyes
      return (
        <g className="baka-eyes">
          <text className="baka-heart-eye" fontSize="7" textAnchor="middle" x="16" y="24">
            &#x2605;
          </text>
          <text className="baka-heart-eye" fontSize="7" textAnchor="middle" x="32" y="24">
            &#x2605;
          </text>
        </g>
      );
    }
    if (state === "dragging") {
      // Surprised eyes (bigger)
      return (
        <g className="baka-eyes">
          <ellipse className="baka-eye" cx="16" cy="22" rx="3.5" ry="4" />
          <ellipse className="baka-eye" cx="32" cy="22" rx="3.5" ry="4" />
          <circle className="baka-pupil" cx="16" cy="21" r="1.8" />
          <circle className="baka-pupil" cx="32" cy="21" r="1.8" />
        </g>
      );
    }
    if (idlePose === "blink" && state === "floating-idle") {
      // Blinking (closed eyes)
      return (
        <g className="baka-eyes baka-blink">
          <line className="baka-eye-closed" x1="13" x2="19" y1="22" y2="22" />
          <line className="baka-eye-closed" x1="29" x2="35" y1="22" y2="22" />
        </g>
      );
    }
    if (idlePose === "yawn" && state === "floating-idle") {
      // Sleepy (half-closed)
      return (
        <g className="baka-eyes">
          <ellipse className="baka-eye" cx="16" cy="22" rx="3" ry="1.5" />
          <ellipse className="baka-eye" cx="32" cy="22" rx="3" ry="1.5" />
          <circle className="baka-pupil" cx="16" cy="22" r="1.2" />
          <circle className="baka-pupil" cx="32" cy="22" r="1.2" />
        </g>
      );
    }
    if (state === "waving-bye") {
      // Happy squint eyes
      return (
        <g className="baka-eyes">
          <path className="baka-eye-arc" d="M 13 22 Q 16 19 19 22" />
          <path className="baka-eye-arc" d="M 29 22 Q 32 19 35 22" />
        </g>
      );
    }
    if (state === "hovered" && hoverPose === "curious") {
      // One eye bigger (curious)
      return (
        <g className="baka-eyes">
          <ellipse className="baka-eye" cx="16" cy="22" rx="3" ry="3.5" />
          <ellipse className="baka-eye" cx="32" cy="22" rx="3.5" ry="4" />
          <circle className="baka-pupil" cx="16" cy="21.5" r="1.5" />
          <circle className="baka-pupil" cx="32.5" cy="21" r="2" />
        </g>
      );
    }

    // Default eyes
    return (
      <g className="baka-eyes">
        <ellipse className="baka-eye" cx="16" cy="22" rx="3" ry="3.2" />
        <ellipse className="baka-eye" cx="32" cy="22" rx="3" ry="3.2" />
        <circle className="baka-pupil" cx="16" cy="21.5" r="1.5" />
        <circle className="baka-pupil" cx="32" cy="21.5" r="1.5" />
        <circle className="baka-eye-highlight" cx="17" cy="20" r="0.8" />
        <circle className="baka-eye-highlight" cx="33" cy="20" r="0.8" />
      </g>
    );
  }, [state, idlePose, hoverPose, clickPose]);

  // Mouth
  const mouth = useMemo(() => {
    if (showWorking) {
      // Focused/determined — a short straight line
      return <line className="baka-mouth-focus" x1="21" x2="27" y1="32" y2="32" />;
    }
    if (state === "waving-bye") {
      // Gentle smile
      return <path className="baka-mouth-smile" d="M 19 30 Q 24 34 29 30" />;
    }
    if (state === "dragging") {
      // Surprised O mouth
      return <ellipse className="baka-mouth-o" cx="24" cy="31" rx="2.5" ry="3" />;
    }
    if (state === "hovered" || state === "chatting") {
      // Big smile
      return <path className="baka-mouth-smile-big" d="M 18 29 Q 24 36 30 29" />;
    }
    if (state === "clicked") {
      // Excited open mouth
      return <path className="baka-mouth-open" d="M 19 29 Q 24 37 29 29 Z" />;
    }
    if (idlePose === "yawn" && state === "floating-idle") {
      // Yawn
      return <ellipse className="baka-mouth-yawn" cx="24" cy="32" rx="3" ry="4" />;
    }

    // Default gentle smile
    return <path className="baka-mouth-smile" d="M 19 30 Q 24 34 29 30" />;
  }, [state, idlePose, showWorking]);

  // Arms
  const arms = useMemo(() => {
    if (showWorking) {
      // Hands held in front of the chest, fluttering rapidly — reads as
      // "frantic typing / busy hands" rather than rhythmic vertical motion.
      return (
        <g className="baka-arms baka-arms-working">
          <g className="baka-work-hand-l">
            <path className="baka-arm" d="M 9 28 Q 13 31 19 32" />
            <circle className="baka-hand" cx="19" cy="32" r="2.6" />
          </g>
          <g className="baka-work-hand-r">
            <path className="baka-arm" d="M 39 28 Q 35 31 29 32" />
            <circle className="baka-hand" cx="29" cy="32" r="2.6" />
          </g>
        </g>
      );
    }
    if (state === "waving-bye") {
      // One arm waving goodbye
      return (
        <g className="baka-arms">
          <path className="baka-arm" d="M 6 28 Q 2 30 3 36" />
          <circle className="baka-hand" cx="3" cy="37" r="2.5" />
          <path className="baka-arm baka-arm-wave-r" d="M 42 28 Q 48 22 44 16" />
          <circle className="baka-hand baka-hand-wave-r" cx="44" cy="15" r="2.5" />
        </g>
      );
    }
    if (state === "peeking") {
      // Waving arm
      return (
        <g className="baka-arms">
          <path className="baka-arm baka-arm-wave" d="M 6 28 Q 0 22 4 16" />
          <circle className="baka-hand baka-hand-wave" cx="4" cy="15" r="2.5" />
        </g>
      );
    }
    if (state === "clicked" && (clickPose === "wave" || clickPose === "heart")) {
      return (
        <g className="baka-arms">
          <path className="baka-arm baka-arm-wave" d="M 6 28 Q 0 22 4 16" />
          <circle className="baka-hand baka-hand-wave" cx="4" cy="15" r="2.5" />
          <path className="baka-arm baka-arm-wave-r" d="M 42 28 Q 48 22 44 16" />
          <circle className="baka-hand baka-hand-wave-r" cx="44" cy="15" r="2.5" />
        </g>
      );
    }
    if (state === "dragging") {
      // Arms up (being held)
      return (
        <g className="baka-arms">
          <path className="baka-arm" d="M 8 20 Q 2 14 4 8" />
          <circle className="baka-hand" cx="4" cy="7" r="2.5" />
          <path className="baka-arm" d="M 40 20 Q 46 14 44 8" />
          <circle className="baka-hand" cx="44" cy="7" r="2.5" />
        </g>
      );
    }

    // Default: arms at sides
    return (
      <g className="baka-arms">
        <path className="baka-arm" d="M 7 26 Q 2 30 3 36" />
        <circle className="baka-hand" cx="3" cy="37" r="2.5" />
        <path className="baka-arm" d="M 41 26 Q 46 30 45 36" />
        <circle className="baka-hand" cx="45" cy="37" r="2.5" />
      </g>
    );
  }, [state, clickPose, showWorking]);

  // Source sits at the belly — clearly below mouth & hands, so it reads as
  // smoke rising from a workpiece, not as something exhaled from the face.
  const smoke = showWorking ? (
    <g className="baka-smoke">
      <circle className="baka-smoke-puff baka-smoke-1" cx="24" cy="40" r="5.5" />
      <circle className="baka-smoke-puff baka-smoke-2" cx="20" cy="40" r="4.8" />
      <circle className="baka-smoke-puff baka-smoke-3" cx="28" cy="40" r="5" />
      <circle className="baka-smoke-puff baka-smoke-4" cx="22" cy="40" r="4.2" />
      <circle className="baka-smoke-puff baka-smoke-5" cx="26" cy="40" r="4.4" />
      <circle className="baka-smoke-puff baka-smoke-6" cx="24" cy="40" r="3.8" />
      <circle className="baka-smoke-puff baka-smoke-7" cx="19" cy="40" r="3.6" />
      <circle className="baka-smoke-puff baka-smoke-8" cx="29" cy="40" r="3.9" />
      <circle className="baka-smoke-puff baka-smoke-9" cx="23" cy="40" r="3.4" />
      <circle className="baka-smoke-puff baka-smoke-10" cx="25" cy="40" r="3.5" />
    </g>
  ) : null;

  // Blush marks (appear on hover/click)
  const blush =
    state === "hovered" || state === "clicked" || state === "chatting" ? (
      <g className="baka-blush">
        <ellipse className="baka-blush-mark" cx="9" cy="28" rx="3" ry="1.8" />
        <ellipse className="baka-blush-mark" cx="39" cy="28" rx="3" ry="1.8" />
      </g>
    ) : null;

  // Little antenna/hair
  const antenna = (
    <g className="baka-antenna">
      <path className="baka-antenna-line" d="M 24 5 Q 22 -2 18 0" />
      <circle className="baka-antenna-tip" cx="17.5" cy="0" r="2" />
    </g>
  );

  return (
    <div
      className={`baka-character ${animClass} edge-${dockedEdge} ${showWorking ? "working" : ""}`}
      style={{ width: BAKA_SIZE, height: BAKA_SIZE }}
    >
      <svg
        height={BAKA_SIZE}
        viewBox="-2 -4 52 52"
        width={BAKA_SIZE}
        xmlns="http://www.w3.org/2000/svg"
      >
        {antenna}
        {/* Body */}
        <ellipse className="baka-body" cx="24" cy="26" rx="18" ry="17" />
        {/* Face */}
        {eyes}
        {mouth}
        {blush}
        {/* Arms */}
        {arms}
        {/* Smoke (working) */}
        {smoke}
      </svg>
    </div>
  );
};

export default React.memo(BakaCharacter);
