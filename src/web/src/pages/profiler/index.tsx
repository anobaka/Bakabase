"use client";

import React from "react";

import envConfig from "@/config/env";

export default function ProfilerPage() {
  const profilerUrl = `${envConfig.apiEndpoint}/profiler/results-index`;

  return (
    <div className="w-full h-full">
      <iframe
        src={profilerUrl}
        className="w-full h-full border-0"
        title="Performance Profiler"
      />
    </div>
  );
}
