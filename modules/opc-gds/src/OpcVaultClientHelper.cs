// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;

namespace Opc.Ua.Gds.Server.OpcVault {
    public static class OpcVaultClientHelper {
        private static readonly int kGuidLength = Guid.Empty.ToString().Length;

        public static string GetServiceIdFromNodeId(NodeId nodeId, ushort namespaceIndex) {
            if (NodeId.IsNull(nodeId)) {
                throw new ArgumentNullException(nameof(nodeId));
            }

            if (namespaceIndex != nodeId.NamespaceIndex) {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            if (nodeId.IdType == IdType.Guid) {
                var id = nodeId.Identifier as Guid?;
                if (id == null) {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }
                return id.ToString();
            }
            else if (nodeId.IdType == IdType.String) {
                if (!(nodeId.Identifier is string id)) {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }
                return id;
            }
            else {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
        }

        public static NodeId GetNodeIdFromServiceId(string nodeIdentifier, ushort namespaceIndex) {
            if (string.IsNullOrEmpty(nodeIdentifier)) {
                throw new ArgumentNullException(nameof(nodeIdentifier));
            }

            if (nodeIdentifier.Length == kGuidLength) {
                try {
                    var nodeGuid = new Guid(nodeIdentifier);
                    return new NodeId(nodeGuid, namespaceIndex);
                }
#pragma warning disable RECS0022 // A catch clause that catches System.Exception and has an empty body
                catch
#pragma warning restore RECS0022 // A catch clause that catches System.Exception and has an empty body
                {
                    // must be string, continue...
                }
            }
            return new NodeId(nodeIdentifier, namespaceIndex);
        }

    }
}

