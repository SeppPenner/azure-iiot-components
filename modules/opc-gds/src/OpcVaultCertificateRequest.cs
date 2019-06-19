// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using Microsoft.Azure.IIoT.OpcUa.Api.Vault;
using Microsoft.Azure.IIoT.OpcUa.Api.Vault.v1;
using Microsoft.Azure.IIoT.OpcUa.Api.Vault.v1.Models;
using Microsoft.Rest;

namespace Opc.Ua.Gds.Server.OpcVault {
    public class OpcVaultCertificateRequest : ICertificateRequest {
        private IVaultServiceApi _opcVaultServiceClient { get; }
        public OpcVaultCertificateRequest(IVaultServiceApi opcVaultServiceClient) {
            _opcVaultServiceClient = opcVaultServiceClient;
        }

        #region ICertificateRequest
        public void Initialize() {
        }

        public ushort NamespaceIndex { get; set; }

        public NodeId StartSigningRequest(
            NodeId applicationId,
            string groupId,
            string certificateType,
            byte[] certificateRequest,
            string authorityId) {
            var appId = OpcVaultClientHelper.GetServiceIdFromNodeId(applicationId, NamespaceIndex);
            if (string.IsNullOrEmpty(appId)) {
                throw new ServiceResultException(StatusCodes.BadNotFound, "The ApplicationId is invalid.");
            }

            if (string.IsNullOrWhiteSpace(certificateType)) {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The CertificateType does not refer to a supported CertificateType.");
            }

            if (string.IsNullOrWhiteSpace(groupId)) {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The GroupId does not refer to a supported CertificateGroup.");
            }

            try {
                var model = new CreateSigningRequestApiModel(
                    appId,
                    groupId,
                    certificateType,
                    Convert.ToBase64String(certificateRequest)
                    );

                var requestId = _opcVaultServiceClient.CreateSigningRequest(model);
                return OpcVaultClientHelper.GetNodeIdFromServiceId(requestId, NamespaceIndex);
            }
            catch (HttpOperationException httpEx) {
                // TODO: return matching ServiceResultException
                //throw new ServiceResultException(StatusCodes.BadNotFound);
                //throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                //throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                //throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                //throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
                throw new ServiceResultException(httpEx, StatusCodes.BadNotSupported);
            }
        }

        public NodeId StartNewKeyPairRequest(
            NodeId applicationId,
            string groupId,
            string certificateType,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword,
            string authorityId) {
            var appId = OpcVaultClientHelper.GetServiceIdFromNodeId(applicationId, NamespaceIndex);
            if (string.IsNullOrEmpty(appId)) {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The ApplicationId is invalid.");
            }

            if (string.IsNullOrWhiteSpace(certificateType)) {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The CertificateType does not refer to a supported CertificateType.");
            }

            if (string.IsNullOrWhiteSpace(groupId)) {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The GroupId does not refer to a supported CertificateGroup.");
            }

            try {
                var model = new CreateNewKeyPairRequestApiModel(
                    appId,
                    groupId,
                    certificateType,
                    subjectName,
                    domainNames,
                    privateKeyFormat,
                    privateKeyPassword
                    );

                var requestId = _opcVaultServiceClient.CreateNewKeyPairRequest(model);

                return OpcVaultClientHelper.GetNodeIdFromServiceId(requestId, NamespaceIndex);
            }
            catch (HttpOperationException httpEx) {
                // TODO: return matching ServiceResultException
                //throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                //throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                //throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                throw new ServiceResultException(httpEx, StatusCodes.BadRequestNotAllowed);
            }

        }

        public void ApproveRequest(
            NodeId requestId,
            bool isRejected
            ) {
            try {
                // intentionally ignore the auto approval, it is implemented in the OpcVault service
                var reqId = OpcVaultClientHelper.GetServiceIdFromNodeId(requestId, NamespaceIndex);
                _opcVaultServiceClient.ApproveCertificateRequest(reqId, isRejected);
            }
            catch (HttpOperationException httpEx) {
                throw new ServiceResultException(httpEx, StatusCodes.BadUserAccessDenied);
            }
        }

        public void AcceptRequest(NodeId requestId, byte[] signedCertificate) {
            try {
                var reqId = OpcVaultClientHelper.GetServiceIdFromNodeId(requestId, NamespaceIndex);
                _opcVaultServiceClient.AcceptCertificateRequest(reqId);
            }
            catch (HttpOperationException httpEx) {
                throw new ServiceResultException(httpEx, StatusCodes.BadUserAccessDenied);
            }

        }

        public CertificateRequestState FinishRequest(
            NodeId applicationId,
            NodeId requestId,
            out string groupId,
            out string certificateType,
            out byte[] signedCertificate,
            out byte[] privateKey) {
            var reqId = OpcVaultClientHelper.GetServiceIdFromNodeId(requestId, NamespaceIndex);
            if (string.IsNullOrEmpty(reqId)) {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The RequestId is invalid.");
            }

            var appId = OpcVaultClientHelper.GetServiceIdFromNodeId(applicationId, NamespaceIndex);
            if (string.IsNullOrEmpty(appId)) {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The ApplicationId is invalid.");
            }

            groupId = null;
            certificateType = null;
            signedCertificate = null;
            privateKey = null;
            try {
                var request = _opcVaultServiceClient.FetchCertificateRequestResult(reqId, appId);
                var state = (CertificateRequestState)Enum.Parse(typeof(CertificateRequestState), request.State.ToString(), true);
                if (state == CertificateRequestState.Approved) {
                    groupId = request.GroupId;
                    certificateType = request.CertificateType;
                    signedCertificate = request.SignedCertificate != null ? Convert.FromBase64String(request.SignedCertificate) : null;
                    privateKey = request.PrivateKey != null ? Convert.FromBase64String(request.PrivateKey) : null;
                }
                return state;
            }
            catch (HttpOperationException httpEx) {
                //throw new ServiceResultException(StatusCodes.BadNotFound);
                //throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                //throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                //throw new ServiceResultException(StatusCodes.BadNothingToDo);
                throw new ServiceResultException(httpEx, StatusCodes.BadRequestNotAllowed);
            }
        }

        public CertificateRequestState ReadRequest(
            NodeId applicationId,
            NodeId requestId,
            out string groupId,
            out string certificateType,
            out byte[] certificateRequest,
            out string subjectName,
            out string[] domainNames,
            out string privateKeyFormat,
            out string privateKeyPassword) => throw new NotImplementedException();
        #endregion
    }
}
