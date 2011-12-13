﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace BizzBingo.Web.Areas.Wiki.Models
{
    public class DetailResourceViewModel
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public int Votes { get; set; }
    }
}