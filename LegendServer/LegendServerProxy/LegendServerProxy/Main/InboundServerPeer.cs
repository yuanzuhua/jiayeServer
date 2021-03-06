﻿using System.Collections.Generic;
using Photon.SocketServer;
using PhotonHostRuntimeInterfaces;
using LegendProtocol;
using LegendServerProxy.Core;
using LegendServer.Util;
using System.Diagnostics;
using Photon.SocketServer.ServerToServer;

namespace LegendServerProxy
{
    //被动建立的连接（内网服务器）
    public class InboundServerPeer : InboundS2SPeer
    {
        public InboundServerPeer(InitRequest initRequest, string serverName, int serverID)
               : base(initRequest)
        {
            if (!string.IsNullOrEmpty(serverName) && serverID > 0)
            {
                SessionManager.Instance.AddInboundServerSession(this.ConnectionId, new InboundServerSession(this, serverName, serverID));
            }
        }
        protected override void OnDisconnect(PhotonHostRuntimeInterfaces.DisconnectReason reasonCode, string reasonDetail)
        {
            int peerId = this.ConnectionId;
            string reason = reasonCode.ToString() + ":" + reasonDetail;
            ServerCPU.Instance.PushCommand(() => SessionManager.Instance.RemoveInboundServerSession(peerId, reason));
        }

        protected override void OnOperationRequest(OperationRequest operationRequest, SendParameters sendParameters)
        {
            switch (operationRequest.OperationCode)
            {
                case (byte)0:
                    {
                        //进入中央处理器的逻辑单线程处理
                        ServerCPU.Instance.PushCommand(() => ProcessMsg(operationRequest.Parameters, sendParameters));
                    }
                    break;
                default:
                    break;
            }
        }

        private bool ProcessMsg(Dictionary<byte, object> operationParameters, SendParameters sendParameters)
        {
            try
            {
                if (operationParameters.Count < 3) return false;

                //取得消息ID，如果非法消息过滤掉
                MsgID msgId = (MsgID)operationParameters[0];
                if (!MsgFactory.IsValidMsg(msgId)) return false;

                //过滤掉非法会话
                InboundServerSession session = SessionManager.Instance.GetInboundServerSessionByPeerId(this.ConnectionId);
                if (session == null) return false;                

                //取得原始消息数据，如果空包则过滤掉
                byte[] msgByte = (byte[])operationParameters[1];

                //根据消息ID取已注册的消息组件
                MsgComponent msgComponent = MsgFactory.allMsg[msgId];

                //反序列化出消息
                object msg = Serializer.tryUncompressMsg(msgByte, (int)operationParameters[2] >= Util.needCompressSize, msgComponent.type);                

                //处理消息
                if (ServerCPU.Instance.ProfilerBound > 0)
                {
                    //处理消息时附加性能统计
                    CodeElapseChecker.CountElapse("Msg:" + msg.ToString(), () => msgComponent.handle(this.ConnectionId, true, msg), ServerCPU.Instance.ProfilerBound, (int)operationParameters[2]);
                }
                else
                {
                    //只处理消息不做性能统计
                    msgComponent.handle(this.ConnectionId, true, msg);
                }
            }
            catch (System.Exception ex)
            {
                ServerUtil.RecordLog(LogType.Error, ex, new StackTrace(new StackFrame(true)).GetFrame(0));
                return false;
            }
            return true;
        }
        protected override void OnOperationResponse(OperationResponse operationResponse, SendParameters sendParameters)
        {
        }

        protected override void OnEvent(IEventData eventData, SendParameters sendParameters)
        {
        }

    }
}
