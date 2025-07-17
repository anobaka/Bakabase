"use client";

import { useTour } from "@reactour/tour";
import React from "react";

export default function Tour() {
  const { isOpen, currentStep, steps, setIsOpen, setCurrentStep, setSteps } =
    useTour();

  console.log("current steps", steps);

  return (
    <>
      <h1>{isOpen ? "Welcome to the tour!" : "Thank you for participate!"}</h1>
      <p className={"new-place-1"}>
        Now you are visiting the place {currentStep + 1} of {steps.length}
      </p>
      <nav>
        <button onClick={() => setIsOpen((o) => !o)}>Toggle Tour</button>
        <button onClick={() => setCurrentStep(3)}>
          Take a fast way to 4th place
        </button>
        <button
          onClick={() => {
            setSteps!([
              { selector: ".new-place-1", content: "New place 1" },
              { selector: ".new-place-2", content: "New place 2" },
            ]);
            setCurrentStep(1);
          }}
        >
          Switch to a new set of places, starting from the last one!
        </button>
      </nav>
    </>
  );
}
