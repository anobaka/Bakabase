import React from 'react';
import ChatView from '@/components/FloatingAssistantV2/components/ChatView';
import './index.scss';

const ChatPage: React.FC = () => {
  return (
    <div className="chat-page">
      <ChatView mode="page" />
    </div>
  );
};

export default ChatPage;
