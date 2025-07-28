import type { ComponentType, ReactNode } from "react";

import React from "react";

import ErrorBoundary from "./index";

// Higher-order component for wrapping components with error boundary
export function withErrorBoundary<P extends object>(
  Component: ComponentType<P>,
  fallback?: (error: Error, errorInfo?: React.ErrorInfo) => ReactNode,
  onError?: (error: Error, errorInfo: React.ErrorInfo) => void,
) {
  const WrappedComponent = (props: P) => (
    <ErrorBoundary fallback={fallback} onError={onError}>
      <Component {...props} />
    </ErrorBoundary>
  );

  WrappedComponent.displayName = `withErrorBoundary(${Component.displayName || Component.name})`;

  return WrappedComponent;
}

// Hook-like API for error boundary (still uses class component internally)
export function useErrorBoundary() {
  return {
    ErrorBoundary,
    withErrorBoundary,
  };
}

export default ErrorBoundary;
