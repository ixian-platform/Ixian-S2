﻿// Copyright (C) 2017-2020 Ixian OU
// This file is part of Ixian DLT - www.github.com/ProjectIxian/Ixian-DLT
//
// Ixian DLT is free software: you can redistribute it and/or modify
// it under the terms of the MIT License as published
// by the Open Source Initiative.
//
// Ixian DLT is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// MIT License for more details.

using IXICore;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Utils;
using S2.Meta;
using System;
using System.Linq;

namespace S2.Network
{
    class InventoryCacheS2 : InventoryCache
    {
        public InventoryCacheS2() : base()
        {
            typeOptions[InventoryItemTypes.block].maxItems = 100;
            typeOptions[InventoryItemTypes.transaction].maxItems = 600000;
            typeOptions[InventoryItemTypes.keepAlive].maxItems = 600000;
        }

        override protected bool sendInventoryRequest(InventoryItem item, RemoteEndpoint endpoint)
        {
            switch (item.type)
            {
                case InventoryItemTypes.block:
                    return handleBlock(item, endpoint);
                case InventoryItemTypes.keepAlive:
                    return handleKeepAlive(item, endpoint);
                case InventoryItemTypes.transaction:
                    CoreProtocolMessage.broadcastGetTransaction(item.hash, 0, endpoint);
                    return true;
                default:
                    Logging.error("Unknown inventory item type {0}", item.type);
                    break;
            }
            return false;
        }

        private bool handleBlock(InventoryItem item, RemoteEndpoint endpoint)
        {
            InventoryItemBlock iib = (InventoryItemBlock)item;
            ulong last_block_height = IxianHandler.getLastBlockHeight();
            if (iib.blockNum == last_block_height + 1)
            {
                Node.tiv.requestNewBlockHeaders(iib.blockNum, endpoint);
                return true;
            }
            return false;
        }

        private bool handleKeepAlive(InventoryItem item, RemoteEndpoint endpoint)
        {
            if (endpoint == null)
            {
                return false;
            }
            InventoryItemKeepAlive iika = (InventoryItemKeepAlive)item;
            byte[] address = iika.address.addressNoChecksum;
            Presence p = PresenceList.getPresenceByAddress(iika.address);
            if (p == null)
            {
                CoreProtocolMessage.broadcastGetPresence(address, endpoint);
                return false;
            }
            else
            {
                var pa = p.addresses.Find(x => x.device.SequenceEqual(iika.deviceId));
                if (pa == null || iika.lastSeen > pa.lastSeenTime)
                {
                    byte[] address_len_bytes = ((ulong)address.Length).GetIxiVarIntBytes();
                    byte[] device_len_bytes = ((ulong)iika.deviceId.Length).GetIxiVarIntBytes();
                    byte[] data = new byte[1 + address_len_bytes.Length + address.Length + device_len_bytes.Length + iika.deviceId.Length];
                    data[0] = 1;
                    Array.Copy(address_len_bytes, 0, data, 1, address_len_bytes.Length);
                    Array.Copy(address, 0, data, 1 + address_len_bytes.Length, address.Length);
                    Array.Copy(device_len_bytes, 0, data, 1 + address_len_bytes.Length + address.Length, device_len_bytes.Length);
                    Array.Copy(iika.deviceId, 0, data, 1 + address_len_bytes.Length + address.Length + device_len_bytes.Length, iika.deviceId.Length);
                    endpoint.sendData(ProtocolMessageCode.getKeepAlives, data, null);
                    return true;
                }
            }
            return false;
        }
    }
}
