// Copyright (C) 2017-2025 Ixian
// This file is part of Ixian Core - www.github.com/ixian-platform/Ixian-Core
//
// Ixian Core is free software: you can redistribute it and/or modify
// it under the terms of the MIT License as published
// by the Open Source Initiative.
//
// Ixian Core is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// MIT License for more details.
namespace IXICore.Inventory
{
    class InventoryCacheS2 : InventoryCacheClient
    {
        public InventoryCacheS2(TransactionInclusion tiv) : base(tiv)
        {
            typeOptions[InventoryItemTypes.keepAlive].maxItems = 1000000;
        }
    }
}
