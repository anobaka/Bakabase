import React from "react";

import ErrorModal from "../Modal";

interface IErrorProps {
  children?: any;
}

interface IErrorState {
  error?: Error;
}

class ErrorBoundary extends React.Component<IErrorProps, IErrorState> {
  constructor(props) {
    super(props);
    this.state = { error: undefined };
  }

  static getDerivedStateFromError(error) {
    // Update state so the next render will show the fallback UI.
    return { error: error };
  }

  componentDidCatch(error, errorInfo) {
    // You can also log the error to an error reporting service
    // logErrorToMyService(error, errorInfo);
    // alert(error.message);
  }

  render() {
    const { error } = this.state;

    if (error) {
      return <ErrorModal />;
    }

    return this.props.children;
  }
}

export default ErrorBoundary;
