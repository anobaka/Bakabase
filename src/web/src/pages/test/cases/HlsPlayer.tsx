"use client";

import React, { useEffect, useRef } from "react";
import Hls from "hls.js";

export default function HlsPlayer({ src }) {
  const videoRef = useRef(null);

  useEffect(() => {
    if (Hls.isSupported()) {
      const hls = new Hls();

      hls.loadSource(src);
      hls.attachMedia(videoRef.current);

      return () => hls.destroy();
    } else if (videoRef.current.canPlayType("application/vnd.apple.mpegurl")) {
      videoRef.current.src = src;
    }
  }, [src]);

  return <video ref={videoRef} controls height={600} width={800} />;
}
