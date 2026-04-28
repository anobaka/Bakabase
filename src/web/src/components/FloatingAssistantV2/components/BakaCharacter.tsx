import React, { useMemo } from 'react';
import type { BakaState, IdlePose, HoverPose, ClickPose, PeekPose, DockedEdge } from '../types';
import { BAKA_SIZE } from '../constants';

interface Props {
  state: BakaState;
  idlePose: IdlePose;
  hoverPose: HoverPose;
  clickPose: ClickPose;
  peekPose: PeekPose;
  dockedEdge: DockedEdge;
}

/**
 * A cute round character (小baka) rendered entirely in SVG + CSS animations.
 * Body is a round blob, with expressive eyes, a small mouth, tiny arms, and blush marks.
 */
const BakaCharacter: React.FC<Props> = ({ state, idlePose, hoverPose, clickPose, peekPose, dockedEdge }) => {
  const animClass = useMemo(() => {
    switch (state) {
      case 'floating-idle': return `idle-${idlePose}`;
      case 'hovered': return `hover-${hoverPose}`;
      case 'clicked': return `click-${clickPose}`;
      case 'peeking': return `peek-${peekPose}`;
      case 'dragging': return 'dragging';
      case 'docked': return 'docked';
      case 'chatting': return 'chatting';
      case 'waving-bye': return 'waving-bye';
      default: return 'idle-breathe';
    }
  }, [state, idlePose, hoverPose, clickPose, peekPose]);

  // Eyes change based on state
  const eyes = useMemo(() => {
    if (state === 'clicked' && clickPose === 'heart') {
      // Heart eyes
      return (
        <g className="baka-eyes">
          <text x="16" y="24" fontSize="7" textAnchor="middle" className="baka-heart-eye">&#x2605;</text>
          <text x="32" y="24" fontSize="7" textAnchor="middle" className="baka-heart-eye">&#x2605;</text>
        </g>
      );
    }
    if (state === 'dragging') {
      // Surprised eyes (bigger)
      return (
        <g className="baka-eyes">
          <ellipse cx="16" cy="22" rx="3.5" ry="4" className="baka-eye" />
          <ellipse cx="32" cy="22" rx="3.5" ry="4" className="baka-eye" />
          <circle cx="16" cy="21" r="1.8" className="baka-pupil" />
          <circle cx="32" cy="21" r="1.8" className="baka-pupil" />
        </g>
      );
    }
    if (idlePose === 'blink' && state === 'floating-idle') {
      // Blinking (closed eyes)
      return (
        <g className="baka-eyes baka-blink">
          <line x1="13" y1="22" x2="19" y2="22" className="baka-eye-closed" />
          <line x1="29" y1="22" x2="35" y2="22" className="baka-eye-closed" />
        </g>
      );
    }
    if (idlePose === 'yawn' && state === 'floating-idle') {
      // Sleepy (half-closed)
      return (
        <g className="baka-eyes">
          <ellipse cx="16" cy="22" rx="3" ry="1.5" className="baka-eye" />
          <ellipse cx="32" cy="22" rx="3" ry="1.5" className="baka-eye" />
          <circle cx="16" cy="22" r="1.2" className="baka-pupil" />
          <circle cx="32" cy="22" r="1.2" className="baka-pupil" />
        </g>
      );
    }
    if (state === 'waving-bye') {
      // Happy squint eyes
      return (
        <g className="baka-eyes">
          <path d="M 13 22 Q 16 19 19 22" className="baka-eye-arc" />
          <path d="M 29 22 Q 32 19 35 22" className="baka-eye-arc" />
        </g>
      );
    }
    if (state === 'hovered' && hoverPose === 'curious') {
      // One eye bigger (curious)
      return (
        <g className="baka-eyes">
          <ellipse cx="16" cy="22" rx="3" ry="3.5" className="baka-eye" />
          <ellipse cx="32" cy="22" rx="3.5" ry="4" className="baka-eye" />
          <circle cx="16" cy="21.5" r="1.5" className="baka-pupil" />
          <circle cx="32.5" cy="21" r="2" className="baka-pupil" />
        </g>
      );
    }
    // Default eyes
    return (
      <g className="baka-eyes">
        <ellipse cx="16" cy="22" rx="3" ry="3.2" className="baka-eye" />
        <ellipse cx="32" cy="22" rx="3" ry="3.2" className="baka-eye" />
        <circle cx="16" cy="21.5" r="1.5" className="baka-pupil" />
        <circle cx="32" cy="21.5" r="1.5" className="baka-pupil" />
        <circle cx="17" cy="20" r="0.8" className="baka-eye-highlight" />
        <circle cx="33" cy="20" r="0.8" className="baka-eye-highlight" />
      </g>
    );
  }, [state, idlePose, hoverPose, clickPose]);

  // Mouth
  const mouth = useMemo(() => {
    if (state === 'waving-bye') {
      // Gentle smile
      return <path d="M 19 30 Q 24 34 29 30" className="baka-mouth-smile" />;
    }
    if (state === 'dragging') {
      // Surprised O mouth
      return <ellipse cx="24" cy="31" rx="2.5" ry="3" className="baka-mouth-o" />;
    }
    if (state === 'hovered' || state === 'chatting') {
      // Big smile
      return <path d="M 18 29 Q 24 36 30 29" className="baka-mouth-smile-big" />;
    }
    if (state === 'clicked') {
      // Excited open mouth
      return <path d="M 19 29 Q 24 37 29 29 Z" className="baka-mouth-open" />;
    }
    if (idlePose === 'yawn' && state === 'floating-idle') {
      // Yawn
      return <ellipse cx="24" cy="32" rx="3" ry="4" className="baka-mouth-yawn" />;
    }
    // Default gentle smile
    return <path d="M 19 30 Q 24 34 29 30" className="baka-mouth-smile" />;
  }, [state, idlePose]);

  // Arms
  const arms = useMemo(() => {
    if (state === 'waving-bye') {
      // One arm waving goodbye
      return (
        <g className="baka-arms">
          <path d="M 6 28 Q 2 30 3 36" className="baka-arm" />
          <circle cx="3" cy="37" r="2.5" className="baka-hand" />
          <path d="M 42 28 Q 48 22 44 16" className="baka-arm baka-arm-wave-r" />
          <circle cx="44" cy="15" r="2.5" className="baka-hand baka-hand-wave-r" />
        </g>
      );
    }
    if (state === 'peeking') {
      // Waving arm
      return (
        <g className="baka-arms">
          <path d="M 6 28 Q 0 22 4 16" className="baka-arm baka-arm-wave" />
          <circle cx="4" cy="15" r="2.5" className="baka-hand baka-hand-wave" />
        </g>
      );
    }
    if (state === 'clicked' && (clickPose === 'wave' || clickPose === 'heart')) {
      return (
        <g className="baka-arms">
          <path d="M 6 28 Q 0 22 4 16" className="baka-arm baka-arm-wave" />
          <circle cx="4" cy="15" r="2.5" className="baka-hand baka-hand-wave" />
          <path d="M 42 28 Q 48 22 44 16" className="baka-arm baka-arm-wave-r" />
          <circle cx="44" cy="15" r="2.5" className="baka-hand baka-hand-wave-r" />
        </g>
      );
    }
    if (state === 'dragging') {
      // Arms up (being held)
      return (
        <g className="baka-arms">
          <path d="M 8 20 Q 2 14 4 8" className="baka-arm" />
          <circle cx="4" cy="7" r="2.5" className="baka-hand" />
          <path d="M 40 20 Q 46 14 44 8" className="baka-arm" />
          <circle cx="44" cy="7" r="2.5" className="baka-hand" />
        </g>
      );
    }
    // Default: arms at sides
    return (
      <g className="baka-arms">
        <path d="M 7 26 Q 2 30 3 36" className="baka-arm" />
        <circle cx="3" cy="37" r="2.5" className="baka-hand" />
        <path d="M 41 26 Q 46 30 45 36" className="baka-arm" />
        <circle cx="45" cy="37" r="2.5" className="baka-hand" />
      </g>
    );
  }, [state, clickPose]);

  // Blush marks (appear on hover/click)
  const blush = (state === 'hovered' || state === 'clicked' || state === 'chatting') ? (
    <g className="baka-blush">
      <ellipse cx="9" cy="28" rx="3" ry="1.8" className="baka-blush-mark" />
      <ellipse cx="39" cy="28" rx="3" ry="1.8" className="baka-blush-mark" />
    </g>
  ) : null;

  // Little antenna/hair
  const antenna = (
    <g className="baka-antenna">
      <path d="M 24 5 Q 22 -2 18 0" className="baka-antenna-line" />
      <circle cx="17.5" cy="0" r="2" className="baka-antenna-tip" />
    </g>
  );

  return (
    <div
      className={`baka-character ${animClass} edge-${dockedEdge}`}
      style={{ width: BAKA_SIZE, height: BAKA_SIZE }}
    >
      <svg
        viewBox="-2 -4 52 52"
        width={BAKA_SIZE}
        height={BAKA_SIZE}
        xmlns="http://www.w3.org/2000/svg"
      >
        {antenna}
        {/* Body */}
        <ellipse cx="24" cy="26" rx="18" ry="17" className="baka-body" />
        {/* Face */}
        {eyes}
        {mouth}
        {blush}
        {/* Arms */}
        {arms}
      </svg>
    </div>
  );
};

export default React.memo(BakaCharacter);
