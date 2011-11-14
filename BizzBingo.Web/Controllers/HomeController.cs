﻿using System;
using System.Linq;
using System.Web.Mvc;
using BizzBingo.Web.Models;
using BizzBingo.Web.Models.Home;

namespace BizzBingo.Web.Controllers
{
    using Helper;

    public class HomeController : BaseController
    {
        [HttpGet]
        public ActionResult Index()
        {
            IndexViewModel model = new IndexViewModel();
            model.Top = Session.Query<Term>().OrderByDescending(x => x.UpVotes).Take(5).ToList();
            model.Newest = Session.Query<Term>().OrderByDescending(x => x.CreatedOn).Take(5).ToList(); ;
            return View(model);
        }

        public ActionResult Contact()
        {
            return View();
        }

        public ActionResult Share(Term term)
        {
            if (string.IsNullOrWhiteSpace(term.Title) || string.IsNullOrWhiteSpace(term.Description))
                return Json(true);

            term.Id = Guid.NewGuid();
            term.CreatedOn = DateTime.UtcNow;
            term.LcId = "1033";
            term.Slug = SearchForSlug(term.Title);
            Session.Store(term);
            Session.SaveChanges();
            return Json(true);
        }

        private string SearchForSlug(string title)
        {
            string slug = title.ToSlug();
            if (IsSlugAlreadyTaken(slug))
            {
                string newSlug = slug + "-I";
                SearchForSlug(newSlug);
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