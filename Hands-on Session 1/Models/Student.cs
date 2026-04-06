using System;
using System.Collections.Generic;
using System.Text;

namespace Hands_on_Session_1.Models
{
    public class Student
    {
        public string Name { get; set; }
        public string Class { get; set; }
        public string Gender { get; set; }
        public List<double> Scores { get; set; }
        public double AverageScore { get; set; }
        public string FinalGrade { get; set; }
    }
}
