import React, { useState } from "react";

import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Spacer,
} from "@/components/bakaui";
import { ErrorBoundary } from "@/components/Error";

const BuggyComponent = ({ shouldThrow }: { shouldThrow: string }) => {
  if (shouldThrow === "render") {
    throw new Error("Test error thrown during render!");
  }

  if (shouldThrow === "undefined") {
    const obj: any = undefined;

    return <div>{obj.nonExistent.property}</div>;
  }

  return <div>Component is working fine!</div>;
};

const AsyncBuggyComponent = () => {
  const [shouldThrow, setShouldThrow] = useState(false);

  if (shouldThrow) {
    throw new Error("Test error with component stack trace!");
  }

  return (
    <div>
      <p>This component will throw an error when the button is clicked:</p>
      <Button color="danger" size="sm" onClick={() => setShouldThrow(true)}>
        Click to throw error
      </Button>
    </div>
  );
};

interface Props {
  onClose?: () => void;
}

const ErrorBoundaryTestPage = ({ onClose }: Props) => {
  const [errorType, setErrorType] = useState<string>("");

  const triggerError = (type: string) => {
    setErrorType(type);
  };

  const reset = () => {
    setErrorType("");
  };

  return (
    <Card className="max-w-2xl mx-auto mt-8">
      <CardHeader>
        <div className="flex justify-between items-center w-full">
          <h3 className="text-lg font-bold">Error Boundary Test</h3>
          {onClose && (
            <Button size="sm" variant="light" onClick={onClose}>
              Close
            </Button>
          )}
        </div>
      </CardHeader>
      <CardBody>
        <div className="space-y-4">
          <p className="text-sm text-gray-600">
            Use these buttons to test different types of errors and see how the
            error boundary handles them:
          </p>

          <div className="grid grid-cols-1 gap-3">
            <Button
              color="danger"
              variant="flat"
              onClick={() => triggerError("render")}
            >
              Throw Error During Render
            </Button>

            <Button
              color="danger"
              variant="flat"
              onClick={() => triggerError("undefined")}
            >
              Trigger Undefined Property Error
            </Button>
          </div>

          <Spacer y={2} />

          <div className="border-t pt-4">
            <h4 className="font-semibold mb-2">Interactive Error Test:</h4>
            <p className="text-sm text-gray-600 mb-2">
              This component is wrapped with ErrorBoundary - click to see the
              error modal:
            </p>
            <ErrorBoundary>
              <AsyncBuggyComponent />
            </ErrorBoundary>
          </div>

          <Spacer y={2} />

          {errorType && (
            <div className="border-t pt-4">
              <div className="flex justify-between items-center mb-2">
                <h4 className="font-semibold">
                  Error Component with ErrorBoundary:
                </h4>
                <Button size="sm" variant="light" onClick={reset}>
                  Reset
                </Button>
              </div>
              <p className="text-sm text-gray-600 mb-2">
                This will trigger the enhanced error modal with stack traces:
              </p>
              <ErrorBoundary>
                <BuggyComponent shouldThrow={errorType} />
              </ErrorBoundary>
            </div>
          )}

          <div className="border-t pt-4 mt-4">
            <h4 className="font-semibold mb-2">
              Async Error Test (Not Caught by ErrorBoundary):
            </h4>
            <p className="text-sm text-gray-600 mb-2">
              Error boundaries don't catch async errors - this will show up in
              console:
            </p>
            <Button
              color="warning"
              variant="flat"
              onClick={() => {
                setTimeout(() => {
                  throw new Error(
                    "Async error - this won't be caught by error boundary",
                  );
                }, 100);
              }}
            >
              Trigger Async Error (Check Console)
            </Button>
          </div>

          <div className="text-xs text-gray-500 mt-4 p-3 bg-blue-50 rounded">
            <p>
              <strong>How to test:</strong>
            </p>
            <p>
              1. Click the buttons above to trigger errors wrapped in
              ErrorBoundary
            </p>
            <p>
              2. You should see your enhanced error modal with error details and
              stack traces
            </p>
            <p>3. The async error will only appear in browser console (F12)</p>
            <p>
              4. The main app ErrorBoundary in BasicLayout will catch any
              unhandled errors throughout the app
            </p>
          </div>
        </div>
      </CardBody>
    </Card>
  );
};

export default ErrorBoundaryTestPage;
