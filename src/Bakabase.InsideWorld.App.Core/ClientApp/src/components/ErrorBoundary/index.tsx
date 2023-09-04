import React from 'react';
import { Button, Dialog } from '@alifd/next';
import i18n from 'i18next';
import './index.scss';

interface IErrorProps {
}

interface IErrorState {
  error?: Error;
  visible: boolean;
}

class ErrorBoundary extends React.Component<IErrorProps, IErrorState> {
  constructor(props) {
    super(props);
    this.state = { error: undefined, visible: true };
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

  close() {
    this.setState({ ...this.state, visible: false });
  }

  render() {
    const { error, visible } = this.state;
    if (error) {
      return (
        <Dialog
          className={'error-boundary'}
          visible={visible}
          v2
          style={{ minWidth: 800 }}
          title={i18n.t('An error occurred')}
          closeMode={['close']}
          onClose={close}
          onCancel={close}
          footer={(
            <Button
              type={'primary'}
              onClick={() => {
                location.href = '/';
              }}
            >
              {i18n.t('Back to homepage')}
            </Button>
          )}
        >
          <div className="tip">
            {i18n.t('Please take a screenshot or copy all contents manually then send it to the developer, thanks')}
          </div>
          <div className="info">
            <div className="key">location</div>
            <div className="value">
              {location.hash}
            </div>
            <div className="key">stack</div>
            <pre className="value">
              {error.stack}
            </pre>
          </div>
        </Dialog>
      );
    }

    return this.props.children;
  }
}

export default ErrorBoundary;
