﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Mvc;
using BizzBingo.Web.Infrastructure;
using BizzBingo.Web.Infrastructure.Raven.Indexes;
using BizzBingo.Web.Models;
using BizzBingo.Web.Models.Home;
using Embedly;
using Embedly.OEmbed;
using Action = BizzBingo.Web.Models.Action;

namespace BizzBingo.Web.Controllers
{
    using Helper;

    public class HomeController : BaseController
    {
        [HttpGet]
        public ActionResult Index(CurrentUserInformation currentUser)
        {
            IndexViewModel model = new IndexViewModel();
            model.Top = Session.Query<Term>().OrderByDescending(x => x.UpVotes).Take(3).ToList();
            model.Bottom = Session.Query<Term>().OrderByDescending(x => x.DownVotes).Take(3).ToList();
            model.Newest = Session.Query<Term>().OrderByDescending(x => x.CreatedOn).Take(3).ToList();
            model.NewestUsers = Session.Query<User>().OrderByDescending(x => x.RegisteredOn).Take(15).ToList();
            
            if(currentUser.IsAuthenticated)
            {
                model.UserName = currentUser.Name;
            }
            return View(model);
        }

        [HttpGet]
        public ActionResult Contact()
        {
            return View();
        }

        public ActionResult Share(Term term, CurrentUserInformation currentUser)
        {
            if (string.IsNullOrWhiteSpace(term.Title))
                return Json(false);

            term.Id = Guid.NewGuid();
            term.CreatedOn = DateTime.UtcNow;
            term.LcId = "1033";
            term.Slug = SearchForSlug(term.Title);

            if(currentUser.IsAuthenticated)
            {
                term.SharedByUserId = currentUser.Id;
                var user = Session.Load<User>(currentUser.Id);
                user.ReputationPoints += ActionPoints.ShareANewTerm;
                
                if(user.ActionFeed == null) user.ActionFeed = new List<Action>();
                user.ActionFeed.Add(new Action() { Time = DateTime.UtcNow, Type = ActionType.ShareANewTerm, TermIdContext = term.Id });

                Session.Store(user);
            }

            Session.Store(term);
            Session.SaveChanges();

            string url = Url.Action("Detail", "Term", new {area = "Wiki", slug = term.Slug});
            dynamic result = new {ToTermUrl = url};
            return Json(result);
        }

        private string SearchForSlug(string title)
        {
            string slug = title.ToSlug();
            if (IsSlugAlreadyTaken(slug))
            {
                string newSlug = slug + "-I";
                return SearchForSlug(newSlug);
            }

            return slug;
        }

        private bool IsSlugAlreadyTaken(string slug)
        {
            var result = Session.Query<Term>().Where(x => x.Slug == slug).SingleOrDefault();
            if (result == null) return false;

            return true;
        }
    }
}
