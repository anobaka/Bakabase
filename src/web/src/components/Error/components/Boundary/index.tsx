import type { ReactNode, ErrorInfo } from "react";

import React from "react";

import ErrorModal from "../Modal";

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: (error: Error, errorInfo?: ErrorInfo) => ReactNode;
  onError?: (error: Error, errorInfo: ErrorInfo) => void;
}

interface ErrorBoundaryState {
  error: Error | null;
  errorInfo: ErrorInfo | null;
}

class ErrorBoundary extends React.Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = {
      error: null,
      errorInfo: null,
    };
  }

  static getDerivedStateFromError(error: Error): Partial<ErrorBoundaryState> {
    // Update state so the next render will show the fallback UI
    return { error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    console.error("Error caught by ErrorBoundary:", error, errorInfo);

    // Update state with error info
    this.setState({ errorInfo });

    // Call optional error handler
    this.props.onError?.(error, errorInfo);
  }

  render(): ReactNode {
    const { error, errorInfo } = this.state;
    const { children, fallback } = this.props;

    if (error) {
      // Use custom fallback if provided, otherwise use default ErrorModal
      if (fallback) {
        return fallback(error, errorInfo || undefined);
      }

      return <ErrorModal error={error} errorInfo={errorInfo || undefined} />;
    }

    return children;
  }
}

export default ErrorBoundary;
