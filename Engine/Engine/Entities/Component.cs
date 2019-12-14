﻿using Foster.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace Foster.Engine
{
    public abstract class Component
    {

        public Entity? Entity { get; internal set; }

        public Transform? Transform => Entity?.Transform;

        internal Component()
        {

        }

        public virtual void Created()
        {

        }

        public virtual void Started()
        {

        }

        public virtual void Destroyed()
        {

        }
    }
}
