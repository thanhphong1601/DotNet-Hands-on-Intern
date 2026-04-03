using Hands_on_Session_1.Models;
using System.Globalization;
using System.Text;
using System.Xml.Serialization;
using System.IO;
using File = System.IO.File;
using static System.Net.WebRequestMethods;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Configs
string inputCsv = "BangDiem.csv";
string mainDomain = "http://localhost:5000";
string getStudentListApi = $"{mainDomain}/api/students";
string getGradeSchemasList = $"{mainDomain}/api/grade-schema";
string summaryReportName = "ReportAll.csv";

HttpClient client = new();


app.MapGet("/api/grade-schema/{classNumber}", (string classNumber) =>
{
    string fileName = $"{classNumber}.csv";

    //check legit file
    if (!File.Exists(fileName))
    {
        return Results.NotFound(new { message = $"Không tìm thấy schema cho khối {classNumber} (Thiếu file {fileName})" });
    }

    try
    {
        var lines = File.ReadAllLines(fileName).Skip(1);

        var schemas = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                var parts = line.Split(',');
                return new GradeSchema
                {
                    ClassName = parts[0].Trim(),
                    MinScore = double.Parse(parts[1], CultureInfo.InvariantCulture),
                    MaxScore = double.Parse(parts[2], CultureInfo.InvariantCulture),
                    Grade = parts[3].Trim()
                };
            })
            .ToList();


        return Results.Ok(schemas);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Lỗi định dạng file CSV", error = ex.Message });
    }
});

app.MapGet("/api/students", () =>
{
    string fileName = inputCsv;

    if (!File.Exists(fileName))
    {
        return Results.NotFound(new { message = "File not found" });
    }

    try
    {
        var lines = File.ReadAllLines(fileName).Skip(1);

        var students = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                var parts = line.Split(',');

                var scores = parts.Skip(3)
                                  .Select(scoreStr => double.Parse(scoreStr.Trim(), CultureInfo.InvariantCulture))
                                  .ToList();

                return new Student
                {
                    Name = parts[0].Trim(),
                    Class = parts[1].Trim(),
                    Gender = parts[2].Trim(),
                    Scores = scores,
                    AverageScore = scores.Count != 0 ? Math.Round(scores.Average(), 2) : 0
                };
            })
            .ToList();

        return Results.Ok(students);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Lỗi xảy ra khi đọc file: ", error = ex.Message });
    }
});

app.MapPost("/api/report", async () =>
{
    //all the reports file to be created
    var generatedReports = new List<string>();

    try
    {
        List<Student> studentList;
        var allGrades = new List<GradeSchema>();

        studentList = await client.GetFromJsonAsync<List<Student>>(getStudentListApi);

        if (studentList == null || studentList.Count == 0)
            return Results.BadRequest(new { message = "Không lấy được dữ liệu danh sách học sinh!" });


        allGrades = await GetGradeListFromStudentClass(studentList);

        // group the classes
        var groupedByClass = studentList.GroupBy(s => s.Class);

        foreach (var classGroup in groupedByClass)
        {
            //create new report for each class
            var classReport = new StringBuilder();
            classReport.AppendLine("Lớp,Tên,Giới Tính,Điểm TB,Xếp Loại");

            foreach (var student in classGroup)
            {
                // loop through each student, get the grade from allSchemas & assign it to student's FinalGrade field
                var grade = allGrades.FirstOrDefault(sc => sc.ClassName == student.Class.Substring(0, 2) &&
                student.AverageScore >= sc.MinScore &&
                student.AverageScore <= sc.MaxScore)?.Grade ?? "F";

                student.FinalGrade = grade;

                //add report inf
                classReport.AppendLine($"{student.Class},{student.Name},{student.Gender},{student.AverageScore},{grade}");
            }

            //final sum for each class
            var summaries = classGroup.GroupBy(s => new { s.Gender, s.FinalGrade })
                                      .OrderBy(g => g.Key.FinalGrade);

            //add each sum to the end of the report
            foreach (var sum in summaries)
            {
                classReport.AppendLine($"Lớp {classGroup.Key} có {sum.Count()} học sinh {sum.Key.Gender} có điểm {sum.Key.FinalGrade}");
            }

            classReport.AppendLine();

            //export to csv
            string classReportName = $"{classGroup.Key}_Report.csv";

            await File.WriteAllTextAsync(classReportName, classReport.ToString(), Encoding.UTF8);


            generatedReports.Add(classReportName);
        }

    }
    catch (HttpRequestException httpReqEx)
    {
        return Results.Problem(title: "Lỗi kết nối đến API bên thứ 3.",
            detail: httpReqEx.Message,
            statusCode: 503
            );
    }
    catch (IOException ioEx)
    {
        return Results.Problem(title: "Lỗi đọc file!",
            detail: ioEx.Message,
            statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Có lỗi xảy ra trong quá trình xử lý!", detail = ex.Message });
    }

    return Results.Ok(new { message = "Báo cáo đã được tạo!", files = generatedReports });

});

app.MapPost("/api/report/all", async () =>
{
    List<Student> studentList = [];
    List<GradeSchema> gradeList = [];
    try
    {
        studentList = await client.GetFromJsonAsync<List<Student>>(getStudentListApi);

        if (studentList == null || studentList.Count == 0)
        {
            return Results.Problem(
                title: "Dữ liệu trống",
                detail: "Không lấy được danh sách học sinh.",
                statusCode: 400);
        }

        gradeList = await GetGradeListFromStudentClass(studentList);
        if (gradeList == null || gradeList.Count == 0)
            return Results.Problem(title: "Có lỗi xãy ra trong quá trình lấy danh sách, hoặc danh sách hiện đang rỗng!",
                detail: "",
                statusCode: 204);

        //assign grade to student
        //may make a function fot this
        foreach (var student in studentList)
        {
            var grade = gradeList.FirstOrDefault(sc =>
                sc.ClassName == student.Class &&
                student.AverageScore >= sc.MinScore &&
                student.AverageScore <= sc.MaxScore)?.Grade ?? "F";

            student.FinalGrade = grade;
        }

        var classReport = new StringBuilder();
        classReport.AppendLine("Lớp,Số lượng,Giới tính,Xếp loại");

        var groupedByClass = studentList.GroupBy(s => s.Class);

        foreach (var studentClass in groupedByClass)
        {
            int studentCount = studentClass.Count();

            var summaries = studentClass.GroupBy(s => new { s.Gender, s.FinalGrade })
                                      .OrderBy(g => g.Key.FinalGrade);

            bool isFirstRowOfClass = true;

            foreach (var sum in summaries)
            {
                string className = isFirstRowOfClass ? studentClass.Key : "";

                classReport.AppendLine($"{className},{sum.Count()},{sum.Key.Gender},{sum.Key.FinalGrade}");

                isFirstRowOfClass = false;
            }

            classReport.AppendLine();
        }

        await File.WriteAllTextAsync(summaryReportName, classReport.ToString(), Encoding.UTF8);

        return Results.Ok(new { message = "Đã thành công tạo file báo cáo", file = classReport });
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception)
    {
        throw;
    }
});



app.MapGet("/test", async () =>
{
    List<Student> studentList = await client.GetFromJsonAsync<List<Student>>("http://localhost:5000/api/students");

    // with each class number, can be 12, 11 or 10, there will be 3 apis to get the grade
    var classNumbers = studentList.Select(s => s.Class.Substring(0, 2)).Distinct();
    var allSchemas = new List<GradeSchema>();

    foreach (var num in classNumbers)
    {
        //get all the grade
        var schemas = await client.GetFromJsonAsync<List<GradeSchema>>($"http://localhost:5000/api/grade-schema/{num}");
        if (schemas != null) allSchemas.AddRange(schemas);
    }



    return Results.Ok($"{allSchemas[0].MinScore.GetType()} - {studentList[0].AverageScore.GetType()}");
});

async Task<List<GradeSchema>> GetGradeListFromStudentClass(List<Student> studentList)
{
    // with each class number, can be 12, 11 or 10, there will be 3 apis to get the grade
    // also add where condition to make sure there is no exception for SubString below
    // may skip the fraud data since it does not match the condition, if can, throw OutOfRangeArgument for SubString

    try
    {
        List<GradeSchema> gradeList = [];

        var classNumbers = studentList
            .Where(s => !string.IsNullOrEmpty(s.Class) && s.Class.Length >= 2)
            .Select(s => s.Class[..2])
            .Distinct();

        foreach (var num in classNumbers)
        {
            //get all the grade
            var grades = await client.GetFromJsonAsync<List<GradeSchema>>($"{getGradeSchemasList}/{num}");
            if (grades != null) gradeList.AddRange(grades);
        }

        return gradeList;
    }
    catch (Exception)
    {
        throw;
    }

}

app.Run("http://localhost:5000");